using Rune.Core.Invocations;
using Rune.Core.Runes;
using Rune.Runtime.Compilation;
using Rune.Runtime.Native;
using Xunit;

namespace Rune.Runtime.Tests;

public sealed class EventRuneRegistrationTests
{
    [Theory]
    [InlineData(RuneEventType.MessageCreate)]
    [InlineData(RuneEventType.MessageDelete)]
    [InlineData(RuneEventType.MessageReactionAdd)]
    [InlineData(RuneEventType.MessageReactionRemove)]
    public async Task Registration_compiles_loads_and_stores_selected_event(
        RuneEventType eventType)
    {
        var registry = new RuneRegistry();
        var compiler = new RecordingCompiler();
        var runtime = new RecordingRuntime();
        var service = CreateService(registry, compiler, runtime);

        var rune = await service.RegisterAsync(
            42,
            "events",
            RuneLanguage.JavaScript,
            eventType,
            "source");

        Assert.Equal(eventType, rune.EventType);
        Assert.Equal(eventType, compiler.LastEventType);
        var load = Assert.Single(runtime.Loads);
        Assert.Equal(rune.Id, load.Id);
        Assert.Equal(eventType, load.Event);
        Assert.Equal("compiled"u8.ToArray(), load.Component);
        Assert.Same(rune, registry.Get(42, "events"));
    }

    [Fact]
    public async Task Failed_native_validation_does_not_register_rune()
    {
        var registry = new RuneRegistry();
        var runtime = new RecordingRuntime
        {
            LoadFailure = new RuneNativeException("invalid component")
        };
        var service = CreateService(registry, new RecordingCompiler(), runtime);

        await Assert.ThrowsAsync<RuneNativeException>(
            async () => await service.RegisterAsync(
                42,
                "broken",
                RuneLanguage.JavaScript,
                RuneEventType.MessageCreate,
                "source"));

        Assert.Null(registry.Get(42, "broken"));
    }

    [Fact]
    public async Task Failed_update_preserves_registered_rune_and_loaded_component()
    {
        var registry = new RuneRegistry();
        var runtime = new RecordingRuntime();
        var service = CreateService(registry, new RecordingCompiler(), runtime);
        var current = await service.RegisterAsync(
            42,
            "reactions",
            RuneLanguage.JavaScript,
            RuneEventType.MessageReactionAdd,
            "old source");
        runtime.LoadFailure = new RuneNativeException("invalid replacement");

        await Assert.ThrowsAsync<RuneNativeException>(
            async () => await service.UpdateAsync(
                current,
                RuneLanguage.JavaScript,
                "new source"));

        Assert.Same(current, registry.Get(42, "reactions"));
        Assert.Single(runtime.Loaded);
        Assert.Equal(current.Wasm, runtime.Loaded[current.Id]);
    }

    [Fact]
    public async Task Disable_remove_and_enable_drive_only_selected_native_component()
    {
        var registry = new RuneRegistry();
        var runtime = new RecordingRuntime();
        var service = CreateService(registry, new RecordingCompiler(), runtime);
        var first = await service.RegisterAsync(
            42,
            "first",
            RuneLanguage.JavaScript,
            RuneEventType.MessageCreate,
            "source");
        var second = await service.RegisterAsync(
            42,
            "second",
            RuneLanguage.JavaScript,
            RuneEventType.MessageDelete,
            "source");

        await service.SetEnabledAsync(42, "first", false);
        Assert.DoesNotContain(first.Id, runtime.Loaded.Keys);
        Assert.Contains(second.Id, runtime.Loaded.Keys);

        await service.SetEnabledAsync(42, "first", true);
        Assert.Contains(first.Id, runtime.Loaded.Keys);

        await service.RemoveAsync(42, "second");
        Assert.Contains(first.Id, runtime.Loaded.Keys);
        Assert.DoesNotContain(second.Id, runtime.Loaded.Keys);
        Assert.Null(registry.Get(42, "second"));
    }

    private static RuneService CreateService(
        RuneRegistry registry,
        ILanguageCompiler compiler,
        IRuneComponentRuntime runtime) =>
        new(registry, new CompilerRegistry([compiler]), runtime);

    private sealed class RecordingCompiler : ILanguageCompiler
    {
        public RuneLanguage Language => RuneLanguage.JavaScript;
        public RuneEventType? LastEventType { get; private set; }

        public ValueTask<CompiledRune> CompileAsync(
            RuneEventType eventType,
            string source,
            CancellationToken cancellationToken = default)
        {
            LastEventType = eventType;
            return ValueTask.FromResult(
                new CompiledRune("compiled"u8.ToArray(), [], eventType, "test"));
        }
    }

    private sealed class RecordingRuntime : IRuneComponentRuntime
    {
        public List<(Guid Id, RuneEventType Event, byte[] Component)> Loads { get; } = [];
        public Dictionary<Guid, byte[]> Loaded { get; } = [];
        public Exception? LoadFailure { get; set; }

        public void LoadComponent(
            Guid runeId,
            RuneEventType eventType,
            ReadOnlySpan<byte> component)
        {
            Loads.Add((runeId, eventType, component.ToArray()));
            if (LoadFailure is not null)
                throw LoadFailure;
            Loaded[runeId] = component.ToArray();
        }

        public bool RemoveComponent(Guid runeId) => Loaded.Remove(runeId);

        public RuneInvocationResult Invoke(
            Guid runeId,
            EventRuneInvocation invocation) =>
            throw new NotSupportedException();
    }
}
