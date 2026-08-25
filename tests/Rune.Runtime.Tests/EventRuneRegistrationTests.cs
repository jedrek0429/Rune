using Rune.Core.Runes;
using Rune.Runtime.Compilation;
using Rune.Runtime.Exceptions;
using Rune.Runtime.Wasm;
using Xunit;

namespace Rune.Runtime.Tests;

public sealed class EventRuneRegistrationTests
{
    [Theory]
    [InlineData(RuneEventType.MessageCreate)]
    [InlineData(RuneEventType.MessageDelete)]
    [InlineData(RuneEventType.MessageReactionAdd)]
    [InlineData(RuneEventType.MessageReactionRemove)]
    public async Task Registration_compiles_and_stores_selected_event(
        RuneEventType eventType)
    {
        var registry = new RuneRegistry();
        var compiler = new RecordingCompiler();
        await using var executor = CreateExecutor();
        var service = CreateService(registry, compiler, executor);

        var rune = await service.RegisterAsync(
            42,
            "events",
            RuneLanguage.JavaScript,
            eventType,
            "source");

        Assert.Equal(eventType, rune.EventType);
        Assert.Equal(eventType, compiler.LastEventType);
        Assert.Same(rune, registry.Get(42, "events"));
    }

    [Fact]
    public async Task Update_compiles_for_and_preserves_registered_event()
    {
        var registry = new RuneRegistry();
        var compiler = new RecordingCompiler();
        await using var executor = CreateExecutor();
        var service = CreateService(registry, compiler, executor);
        var current = await service.RegisterAsync(
            42,
            "reactions",
            RuneLanguage.JavaScript,
            RuneEventType.MessageReactionAdd,
            "old source");

        var updated = await service.UpdateAsync(
            current,
            RuneLanguage.JavaScript,
            "new source");

        Assert.Equal(
            RuneEventType.MessageReactionAdd,
            compiler.LastEventType);
        Assert.Equal(current.EventType, updated.EventType);
        Assert.Equal("new source", updated.Source);
    }

    [Fact]
    public async Task Failed_update_leaves_registered_rune_unchanged()
    {
        var registry = new RuneRegistry();
        var compiler = new RecordingCompiler();
        await using var executor = CreateExecutor();
        var service = CreateService(registry, compiler, executor);
        var current = await service.RegisterAsync(
            42,
            "deletions",
            RuneLanguage.JavaScript,
            RuneEventType.MessageDelete,
            "working source");
        compiler.Failure = new RuneCompilationException("rejected");

        await Assert.ThrowsAsync<RuneCompilationException>(
            async () =>
            {
                _ = await service.UpdateAsync(
                    current,
                    RuneLanguage.JavaScript,
                    "broken source");
            });

        Assert.Same(current, registry.Get(42, "deletions"));
    }

    [Fact]
    public void Registry_selects_only_enabled_runes_for_guild_and_event()
    {
        var registry = new RuneRegistry();
        var selected = Rune(
            42,
            "selected",
            RuneEventType.MessageReactionRemove,
            enabled: true);

        registry.Add(selected);
        registry.Add(Rune(
            42,
            "wrong-event",
            RuneEventType.MessageCreate,
            enabled: true));
        registry.Add(Rune(
            7,
            "wrong-guild",
            RuneEventType.MessageReactionRemove,
            enabled: true));
        registry.Add(Rune(
            42,
            "disabled",
            RuneEventType.MessageReactionRemove,
            enabled: false));

        var runes = registry.GetEventRunes(
            42,
            RuneEventType.MessageReactionRemove);

        Assert.Collection(
            runes,
            rune => Assert.Same(selected, rune));
    }

    private static RuneService CreateService(
        RuneRegistry registry,
        ILanguageCompiler compiler,
        RuneExecutor executor)
    {
        return new RuneService(
            registry,
            new CompilerRegistry([compiler]),
            executor);
    }

    private static RuneExecutor CreateExecutor()
    {
        var options = new RuneRuntimeOptions();
        return new RuneExecutor(
            new RuneWasmCache(options),
            options);
    }

    private static RegisteredRune Rune(
        ulong guildId,
        string name,
        RuneEventType eventType,
        bool enabled)
    {
        return new RegisteredRune(
            Guid.NewGuid(),
            guildId,
            name,
            RuneLanguage.JavaScript,
            eventType,
            "source",
            [0],
            enabled);
    }

    private sealed class RecordingCompiler : ILanguageCompiler
    {
        public RuneLanguage Language => RuneLanguage.JavaScript;

        public RuneEventType? LastEventType { get; private set; }

        public Exception? Failure { get; set; }

        public ValueTask<CompiledRune> CompileAsync(
            RuneEventType eventType,
            string source,
            CancellationToken cancellationToken = default)
        {
            LastEventType = eventType;

            if (Failure is not null)
                return ValueTask.FromException<CompiledRune>(Failure);

            return ValueTask.FromResult(
                new CompiledRune([1, 2, 3], []));
        }
    }
}
