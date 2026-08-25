using System.Text;
using Rune.Runtime.Native;
using Xunit;

namespace Rune.Runtime.Native.IntegrationTests;

public sealed class NativeRuntimeContractTests
{
    [Fact]
    public void Native_runtime_exposes_supported_abi_version()
    {
        Assert.Equal(3U, RuneNativeRuntime.AbiVersion);
    }

    [Fact]
    public void Component_actions_cross_the_native_boundary_in_order()
    {
        var componentPath = Path.Combine(
            AppContext.BaseDirectory,
            "message_create_component.wasm");

        using var runtime = new RuneNativeRuntime();
        runtime.LoadComponent(File.ReadAllBytes(componentPath));

        var result = runtime.InvokeMessageCreate("Ada");

        Assert.Collection(
            result.Actions,
            action =>
            {
                Assert.Equal(RuneActionKind.Reply, action.Kind);
                Assert.Equal("Hello, Ada!", action.Content);
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
        runtime.LoadComponent(File.ReadAllBytes(componentPath));

        var exception = Assert.Throws<RuneNativeException>(
            () => runtime.InvokeMessageCreate("trap"));

        Assert.Equal(2, exception.NativeStatus);
        Assert.Contains(
            "wasm",
            exception.NativeDetail,
            StringComparison.OrdinalIgnoreCase);

        var recovered = runtime.InvokeMessageCreate("Ada");

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
        runtime.LoadComponent(File.ReadAllBytes(componentPath));

        var exception = Assert.Throws<RuneNativeException>(
            () => runtime.InvokeMessageCreate("fuel"));

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
        runtime.LoadComponent(File.ReadAllBytes(componentPath));

        var exception = Assert.Throws<RuneNativeException>(
            () => runtime.InvokeMessageCreate("memory"));

        Assert.Equal(2, exception.NativeStatus);
        Assert.Contains(
            "memory",
            exception.NativeDetail,
            StringComparison.OrdinalIgnoreCase);

        var recovered = runtime.InvokeMessageCreate("Ada");

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
        runtime.LoadComponent(File.ReadAllBytes(componentPath));

        var result = runtime.InvokeMessageCreate("output-boundary");

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
        runtime.LoadComponent(File.ReadAllBytes(componentPath));

        var exception = Assert.Throws<RuneNativeException>(
            () => runtime.InvokeMessageCreate("action-limit"));

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
        runtime.LoadComponent(File.ReadAllBytes(componentPath));

        var exception = Assert.Throws<RuneNativeException>(
            () => runtime.InvokeMessageCreate("reply-size-limit"));

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
        runtime.LoadComponent(File.ReadAllBytes(componentPath));

        var exception = Assert.Throws<RuneNativeException>(
            () => runtime.InvokeMessageCreate("total-output-limit"));

        Assert.Equal(2, exception.NativeStatus);
        Assert.Contains(
            "output",
            exception.NativeDetail,
            StringComparison.OrdinalIgnoreCase);

        AssertCleanRecovery(runtime);
    }

    private static void AssertCleanRecovery(RuneNativeRuntime runtime)
    {
        var recovered = runtime.InvokeMessageCreate("Ada");

        Assert.DoesNotContain(
            recovered.Actions,
            action => action.Content == "discard me");
        Assert.Equal(2, recovered.Actions.Count);
    }
}
