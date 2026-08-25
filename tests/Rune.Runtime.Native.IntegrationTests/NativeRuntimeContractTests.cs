using System.Text;
using Rune.Core.Invocations;
using Rune.Core.Runes;
using Rune.Runtime.Native;
using Xunit;

namespace Rune.Runtime.Native.IntegrationTests;

public sealed class NativeRuntimeContractTests
{
    [Fact]
    public void Native_runtime_exposes_supported_abi_version()
    {
        Assert.Equal(5U, RuneNativeRuntime.AbiVersion);
    }

    [Fact]
    public void Components_for_multiple_rune_ids_coexist()
    {
        var createId = Guid.NewGuid();
        var deleteId = Guid.NewGuid();
        using var runtime = new RuneNativeRuntime();

        runtime.LoadComponent(
            createId,
            RuneEventType.MessageCreate,
            Fixture("message_create_component.wasm"));
        runtime.LoadComponent(
            deleteId,
            RuneEventType.MessageDelete,
            Fixture("message_delete_component.wasm"));

        Assert.Equal(
            2,
            runtime.Invoke(createId, MessageCreate("Ada")).Actions.Count);
        Assert.Single(
            runtime.Invoke(
                deleteId,
                new MessageDeleteEventRuneInvocation(
                    Guid.NewGuid(),
                    1,
                    2,
                    3)).Actions);
        Assert.Equal(
            2,
            runtime.Invoke(createId, MessageCreate("Grace")).Actions.Count);
    }

    [Fact]
    public void Removing_one_component_does_not_remove_another()
    {
        var removedId = Guid.NewGuid();
        var retainedId = Guid.NewGuid();
        using var runtime = new RuneNativeRuntime();

        runtime.LoadComponent(
            removedId,
            RuneEventType.MessageCreate,
            Fixture("message_create_component.wasm"));
        runtime.LoadComponent(
            retainedId,
            RuneEventType.MessageCreate,
            Fixture("message_create_component.wasm"));

        Assert.True(runtime.RemoveComponent(removedId));
        Assert.Throws<RuneNativeException>(
            () => runtime.Invoke(removedId, MessageCreate("Ada")));
        Assert.Equal(
            2,
            runtime.Invoke(retainedId, MessageCreate("Ada")).Actions.Count);
    }

    [Fact]
    public void Invocation_event_must_match_loaded_component_world()
    {
        var runeId = Guid.NewGuid();
        using var runtime = new RuneNativeRuntime();
        runtime.LoadComponent(
            runeId,
            RuneEventType.MessageCreate,
            Fixture("message_create_component.wasm"));

        Assert.Throws<RuneNativeException>(
            () => runtime.Invoke(
                runeId,
                new MessageDeleteEventRuneInvocation(
                    Guid.NewGuid(),
                    1,
                    2,
                    3)));
    }

    [Fact]
    public void Component_actions_cross_the_native_boundary_in_order()
    {
        var componentPath = Path.Combine(
            AppContext.BaseDirectory,
            "message_create_component.wasm");

        using var runtime = new RuneNativeRuntime();
        runtime.LoadComponent(
            RuneEventType.MessageCreate,
            File.ReadAllBytes(componentPath));

        var result = runtime.Invoke(MessageCreate("Ada"));

        Assert.Collection(
            result.Actions,
            action =>
            {
                Assert.Equal(RuneActionKind.Reply, action.Kind);
                Assert.Equal(
                    "111111111111111111|222222222222222222|" +
                    "hello from Rune|333333333333333333|Ada",
                    action.Content);
            },
            action =>
            {
                Assert.Equal(RuneActionKind.Reply, action.Kind);
                Assert.Equal("Welcome to Rune.", action.Content);
            });
    }

    [Fact]
    public void Failed_component_discards_actions_and_reports_native_detail()
    {
        var componentPath = Path.Combine(
            AppContext.BaseDirectory,
            "message_create_component.wasm");

        using var runtime = new RuneNativeRuntime();
        runtime.LoadComponent(
            RuneEventType.MessageCreate,
            File.ReadAllBytes(componentPath));

        var exception = Assert.Throws<RuneNativeException>(
            () => runtime.Invoke(MessageCreate("trap")));

        Assert.Equal(2, exception.NativeStatus);
        Assert.Contains(
            "wasm",
            exception.NativeDetail,
            StringComparison.OrdinalIgnoreCase);

        var recovered = runtime.Invoke(MessageCreate("Ada"));

        Assert.DoesNotContain(
            recovered.Actions,
            action => action.Content == "discard me");
        Assert.Equal(2, recovered.Actions.Count);
    }

    [Fact]
    public void Cpu_bound_component_is_stopped_by_fuel_budget()
    {
        var componentPath = Path.Combine(
            AppContext.BaseDirectory,
            "message_create_component.wasm");

        using var runtime = new RuneNativeRuntime();
        runtime.LoadComponent(
            RuneEventType.MessageCreate,
            File.ReadAllBytes(componentPath));

        var exception = Assert.Throws<RuneNativeException>(
            () => runtime.Invoke(MessageCreate("fuel")));

        Assert.Equal(2, exception.NativeStatus);
        Assert.Contains(
            "fuel",
            exception.NativeDetail,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Memory_hungry_component_is_stopped_without_leaking_buffered_actions()
    {
        var componentPath = Path.Combine(
            AppContext.BaseDirectory,
            "message_create_component.wasm");

        using var runtime = new RuneNativeRuntime();
        runtime.LoadComponent(
            RuneEventType.MessageCreate,
            File.ReadAllBytes(componentPath));

        var exception = Assert.Throws<RuneNativeException>(
            () => runtime.Invoke(MessageCreate("memory")));

        Assert.Equal(2, exception.NativeStatus);
        Assert.Contains(
            "memory",
            exception.NativeDetail,
            StringComparison.OrdinalIgnoreCase);

        var recovered = runtime.Invoke(MessageCreate("Ada"));

        Assert.DoesNotContain(
            recovered.Actions,
            action => action.Content == "discard me");
        Assert.Equal(2, recovered.Actions.Count);
    }

    [Fact]
    public void Component_at_output_budget_succeeds()
    {
        var componentPath = Path.Combine(
            AppContext.BaseDirectory,
            "message_create_component.wasm");

        using var runtime = new RuneNativeRuntime();
        runtime.LoadComponent(
            RuneEventType.MessageCreate,
            File.ReadAllBytes(componentPath));

        var result = runtime.Invoke(MessageCreate("output-boundary"));

        Assert.Equal(16, result.Actions.Count);
        Assert.All(
            result.Actions,
            action => Assert.Equal(RuneActionKind.Reply, action.Kind));
        Assert.Equal(
            8 * 1024,
            Encoding.UTF8.GetByteCount(result.Actions[0].Content));
        Assert.Equal(
            64 * 1024,
            result.Actions.Sum(action => Encoding.UTF8.GetByteCount(action.Content)));
    }

    [Fact]
    public void Component_exceeding_action_count_is_rejected_transactionally()
    {
        var componentPath = Path.Combine(
            AppContext.BaseDirectory,
            "message_create_component.wasm");

        using var runtime = new RuneNativeRuntime();
        runtime.LoadComponent(
            RuneEventType.MessageCreate,
            File.ReadAllBytes(componentPath));

        var exception = Assert.Throws<RuneNativeException>(
            () => runtime.Invoke(MessageCreate("action-limit")));

        Assert.Equal(2, exception.NativeStatus);
        Assert.Contains(
            "action",
            exception.NativeDetail,
            StringComparison.OrdinalIgnoreCase);

        AssertCleanRecovery(runtime);
    }

    [Fact]
    public void Component_exceeding_reply_size_is_rejected_transactionally()
    {
        var componentPath = Path.Combine(
            AppContext.BaseDirectory,
            "message_create_component.wasm");

        using var runtime = new RuneNativeRuntime();
        runtime.LoadComponent(
            RuneEventType.MessageCreate,
            File.ReadAllBytes(componentPath));

        var exception = Assert.Throws<RuneNativeException>(
            () => runtime.Invoke(MessageCreate("reply-size-limit")));

        Assert.Equal(2, exception.NativeStatus);
        Assert.Contains(
            "reply",
            exception.NativeDetail,
            StringComparison.OrdinalIgnoreCase);

        AssertCleanRecovery(runtime);
    }

    [Fact]
    public void Component_exceeding_total_output_size_is_rejected_transactionally()
    {
        var componentPath = Path.Combine(
            AppContext.BaseDirectory,
            "message_create_component.wasm");

        using var runtime = new RuneNativeRuntime();
        runtime.LoadComponent(
            RuneEventType.MessageCreate,
            File.ReadAllBytes(componentPath));

        var exception = Assert.Throws<RuneNativeException>(
            () => runtime.Invoke(MessageCreate("total-output-limit")));

        Assert.Equal(2, exception.NativeStatus);
        Assert.Contains(
            "output",
            exception.NativeDetail,
            StringComparison.OrdinalIgnoreCase);

        AssertCleanRecovery(runtime);
    }

    [Fact]
    public void Supported_component_imports_are_accepted()
    {
        var componentPath = Path.Combine(
            AppContext.BaseDirectory,
            "message_create_component.wasm");

        using var runtime = new RuneNativeRuntime();

        runtime.LoadComponent(
            RuneEventType.MessageCreate,
            File.ReadAllBytes(componentPath));
        var result = runtime.Invoke(MessageCreate("Ada"));

        Assert.Equal(2, result.Actions.Count);
    }

    [Fact]
    public void Unknown_host_import_is_rejected_during_load()
    {
        using var runtime = new RuneNativeRuntime();

        var exception = Assert.Throws<RuneNativeException>(
            () => runtime.LoadComponent(
                RuneEventType.MessageCreate,
                ComponentImporting("example:forbidden/host@1.0.0")));

        Assert.Equal(2, exception.NativeStatus);
    }

    [Fact]
    public void Unapproved_rune_import_is_rejected_during_load()
    {
        using var runtime = new RuneNativeRuntime();

        var exception = Assert.Throws<RuneNativeException>(
            () => runtime.LoadComponent(
                RuneEventType.MessageCreate,
                ComponentImporting("rune:spike/administration@0.1.0")));

        Assert.Equal(2, exception.NativeStatus);
    }

    [Fact]
    public void Socket_import_is_rejected_during_load()
    {
        using var runtime = new RuneNativeRuntime();

        var exception = Assert.Throws<RuneNativeException>(
            () => runtime.LoadComponent(
                RuneEventType.MessageCreate,
                ComponentImporting("wasi:sockets/tcp@0.2.6")));

        Assert.Equal(2, exception.NativeStatus);
    }

    [Fact]
    public void Rejected_component_does_not_replace_loaded_component()
    {
        var componentPath = Path.Combine(
            AppContext.BaseDirectory,
            "message_create_component.wasm");

        using var runtime = new RuneNativeRuntime();
        runtime.LoadComponent(
            RuneEventType.MessageCreate,
            File.ReadAllBytes(componentPath));

        Assert.Throws<RuneNativeException>(
            () => runtime.LoadComponent(
                RuneEventType.MessageCreate,
                ComponentImporting("example:forbidden/host@1.0.0")));

        var result = runtime.Invoke(MessageCreate("Ada"));

        Assert.Collection(
            result.Actions,
            action => Assert.Contains("|Ada", action.Content),
            action => Assert.Equal("Welcome to Rune.", action.Content));
    }

    [Fact]
    public void Message_delete_arguments_cross_the_native_boundary()
    {
        using var runtime = Load(
            RuneEventType.MessageDelete,
            "message_delete_component.wasm");

        var result = runtime.Invoke(
            new MessageDeleteEventRuneInvocation(
                Guid.NewGuid(),
                444444444444444444,
                555555555555555555,
                666666666666666666));

        Assert.Equal(
            "555555555555555555|444444444444444444|666666666666666666",
            Assert.Single(result.Actions).Content);
    }

    [Fact]
    public void Component_for_another_event_is_rejected_during_load()
    {
        using var runtime = new RuneNativeRuntime();

        var exception = Assert.Throws<RuneNativeException>(
            () => runtime.LoadComponent(
                RuneEventType.MessageCreate,
                File.ReadAllBytes(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "message_delete_component.wasm"))));

        Assert.Equal(2, exception.NativeStatus);
    }

    [Fact]
    public void Message_reaction_add_arguments_cross_the_native_boundary()
    {
        using var runtime = Load(
            RuneEventType.MessageReactionAdd,
            "message_reaction_add_component.wasm");

        var result = runtime.Invoke(
            new MessageReactionAddEventRuneInvocation(
                Guid.NewGuid(),
                777777777777777777,
                888888888888888888,
                999999999999999999,
                111111111111111112,
                222222222222222223,
                new MessageReactionEmojiInvocation(
                    true,
                    333333333333333334,
                    "party"),
                true,
                1));

        Assert.Equal(
            "true|888888888888888888|true|333333333333333334|party|" +
            "777777777777777777|222222222222222223|999999999999999999|" +
            "burst|111111111111111112",
            Assert.Single(result.Actions).Content);
    }

    [Fact]
    public void Message_reaction_remove_arguments_cross_the_native_boundary()
    {
        using var runtime = Load(
            RuneEventType.MessageReactionRemove,
            "message_reaction_remove_component.wasm");

        var result = runtime.Invoke(
            new MessageReactionRemoveEventRuneInvocation(
                Guid.NewGuid(),
                777777777777777777,
                888888888888888888,
                999999999999999999,
                111111111111111112,
                new MessageReactionEmojiInvocation(
                    false,
                    null,
                    "⬆️"),
                false,
                0));

        Assert.Equal(
            "false|888888888888888888|false|none|⬆️|" +
            "777777777777777777|999999999999999999|normal|" +
            "111111111111111112",
            Assert.Single(result.Actions).Content);
    }

    private static byte[] ComponentImporting(string importName)
    {
        return Encoding.UTF8.GetBytes(
            "(component\n"
            + $"    (import \"{importName}\" (type (sub resource)))\n"
            + ")");
    }

    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, name));

    private static void AssertCleanRecovery(RuneNativeRuntime runtime)
    {
        var recovered = runtime.Invoke(MessageCreate("Ada"));

        Assert.DoesNotContain(
            recovered.Actions,
            action => action.Content == "discard me");
        Assert.Equal(2, recovered.Actions.Count);
    }

    private static MessageCreateEventRuneInvocation MessageCreate(
        string username)
    {
        return new MessageCreateEventRuneInvocation(
            Guid.NewGuid(),
            444444444444444444,
            222222222222222222,
            111111111111111111,
            333333333333333333,
            username,
            "hello from Rune");
    }

    private static RuneNativeRuntime Load(
        RuneEventType eventType,
        string fixture)
    {
        var runtime = new RuneNativeRuntime();
        runtime.LoadComponent(
            eventType,
            File.ReadAllBytes(
                Path.Combine(AppContext.BaseDirectory, fixture)));
        return runtime;
    }
}
