using Rune.Core.Invocations;
using Rune.Core.Runes;
using Rune.Runtime;

namespace Rune.Firecracker.Tests;

public sealed class FirecrackerDispatchTests
{
    [Fact]
    public async Task DispatchQueuesOnlyEnabledBuiltRunesForTheSameGuildAndEvent()
    {
        var registry = new RuneRegistry();
        var matching = CreateRune(
            "matching",
            guildId: 42,
            RuneEventType.MessageCreate,
            enabled: true,
            artifact: BuiltArtifact("matching"));

        registry.Add(matching);
        registry.Add(CreateRune(
            "disabled",
            guildId: 42,
            RuneEventType.MessageCreate,
            enabled: false,
            artifact: BuiltArtifact("disabled")));
        registry.Add(CreateRune(
            "other-event",
            guildId: 42,
            RuneEventType.MessageDelete,
            enabled: true,
            artifact: BuiltArtifact("other-event")));
        registry.Add(CreateRune(
            "other-guild",
            guildId: 99,
            RuneEventType.MessageCreate,
            enabled: true,
            artifact: BuiltArtifact("other-guild")));

        var transport = new RecordingTransport();
        var dispatcher = new RuneEventDispatcher(registry, transport);
        var invocation = new MessageCreateEventRuneInvocation(
            Guid.NewGuid(),
            42,
            100,
            200,
            300,
            "Ada",
            "!ping");

        var failures = await dispatcher.DispatchAsync(invocation);

        Assert.Empty(failures);
        var envelope = Assert.Single(transport.Attempts);
        Assert.NotEqual(Guid.Empty, envelope.ExecutionId);
        Assert.Equal(invocation.InvocationId, envelope.InvocationId);
        Assert.Equal(matching.Id, envelope.RuneId);
        Assert.Equal(matching.Name, envelope.RuneName);
        Assert.Equal(matching.Artifact, envelope.Artifact);
        Assert.Equal(RuneLanguage.JavaScript, envelope.Language);
        Assert.Equal(RuneEventType.MessageCreate, envelope.EventType);
        Assert.Equal("200", envelope.Payload.GetProperty("id").GetString());
        Assert.Equal("100", envelope.Payload.GetProperty("channelId").GetString());
        Assert.Equal(
            "300",
            envelope.Payload.GetProperty("author").GetProperty("id").GetString());
        Assert.Equal("!ping", envelope.Payload.GetProperty("content").GetString());
    }

    [Fact]
    public async Task QueueFailureIsIsolatedAndDoesNotPreventOtherRunesFromDispatching()
    {
        var registry = new RuneRegistry();
        var failed = CreateRune(
            "failed",
            guildId: 42,
            RuneEventType.MessageCreate,
            enabled: true,
            artifact: BuiltArtifact("failed"));
        var healthy = CreateRune(
            "healthy",
            guildId: 42,
            RuneEventType.MessageCreate,
            enabled: true,
            artifact: BuiltArtifact("healthy"));
        registry.Add(failed);
        registry.Add(healthy);

        var transport = new RecordingTransport
        {
            FailRuneId = failed.Id
        };
        var dispatcher = new RuneEventDispatcher(registry, transport);

        var failures = await dispatcher.DispatchAsync(
            new MessageCreateEventRuneInvocation(
                Guid.NewGuid(),
                42,
                100,
                200,
                300,
                "Ada",
                "hello"));

        var failure = Assert.Single(failures);
        Assert.Equal("failed", failure.RuneName);
        Assert.Equal("The invocation could not be queued.", failure.Message);
        Assert.Equal(2, transport.Attempts.Count);
        Assert.Contains(transport.Attempts, envelope => envelope.RuneId == failed.Id);
        Assert.Contains(transport.Attempts, envelope => envelope.RuneId == healthy.Id);
    }

    private static RegisteredRune CreateRune(
        string name,
        ulong guildId,
        RuneEventType eventType,
        bool enabled,
        BuiltRuneArtifact? artifact = null,
        string source = "source") =>
        new(
            Guid.NewGuid(),
            guildId,
            name,
            RuneLanguage.JavaScript,
            eventType,
            source,
            enabled,
            artifact);

    private static BuiltRuneArtifact BuiltArtifact(string name) =>
        new($"artifact-{name}", $"sha256:{name}", "rune");

    private sealed class RecordingTransport : IRuneTransport
    {
        public Guid? FailRuneId { get; init; }

        public List<RuneInvocationEnvelope> Attempts { get; } = [];

        public ValueTask EnqueueAsync(
            RuneInvocationEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Attempts.Add(envelope);

            if (envelope.RuneId == FailRuneId)
                throw new InvalidOperationException("Redis unavailable");

            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<RuneResultMessage>> ReadResultsAsync(
            string consumerName,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask AcknowledgeResultAsync(
            string streamId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
