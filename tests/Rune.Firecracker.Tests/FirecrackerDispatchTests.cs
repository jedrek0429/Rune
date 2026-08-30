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
        var matching = CreateRune("matching", 42, RuneEventType.MessageCreate, true);

        registry.Add(matching);
        registry.Add(CreateRune("disabled", 42, RuneEventType.MessageCreate, false));
        registry.Add(CreateRune("other-event", 42, RuneEventType.MessageDelete, true));
        registry.Add(CreateRune("other-guild", 99, RuneEventType.MessageCreate, true));

        var transport = new RecordingTransport();
        var dispatcher = new RuneEventDispatcher(registry, transport);
        var invocation = new MessageCreateEventRuneInvocation(
            Guid.NewGuid(), 42, 100, 200, 300, "Ada", "!ping");

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
    }

    [Fact]
    public async Task UnbuiltRuneIsRejectedBeforeQueueing()
    {
        var registry = new RuneRegistry();
        registry.Add(new RegisteredRune(
            Guid.NewGuid(), 42, "unbuilt", RuneLanguage.JavaScript,
            RuneEventType.MessageCreate, "source", true, null));
        var transport = new RecordingTransport();
        var dispatcher = new RuneEventDispatcher(registry, transport);

        var failures = await dispatcher.DispatchAsync(CreateInvocation());

        var failure = Assert.Single(failures);
        Assert.Equal("unbuilt", failure.RuneName);
        Assert.Equal("The rune has not been built yet.", failure.Message);
        Assert.Empty(transport.Attempts);
    }

    [Fact]
    public async Task OversizedArtifactIsRejectedBeforeQueueing()
    {
        var registry = new RuneRegistry();
        registry.Add(CreateRune(
            "huge", 42, RuneEventType.MessageCreate, true,
            new BuiltRuneArtifact(
                "artifact-huge", "sha256:test", "rune",
                RuneResourceLimits.MaxArtifactBytes + 1L)));
        var transport = new RecordingTransport();
        var dispatcher = new RuneEventDispatcher(registry, transport);

        var failures = await dispatcher.DispatchAsync(CreateInvocation());

        var failure = Assert.Single(failures);
        Assert.Equal("huge", failure.RuneName);
        Assert.Equal("The built rune artifact exceeds the 16 MiB limit.", failure.Message);
        Assert.Empty(transport.Attempts);
    }

    [Fact]
    public async Task QueueFailureIsIsolatedAndDoesNotPreventOtherRunesFromDispatching()
    {
        var registry = new RuneRegistry();
        var failed = CreateRune("failed", 42, RuneEventType.MessageCreate, true);
        var healthy = CreateRune("healthy", 42, RuneEventType.MessageCreate, true);
        registry.Add(failed);
        registry.Add(healthy);

        var transport = new RecordingTransport { FailRuneId = failed.Id };
        var dispatcher = new RuneEventDispatcher(registry, transport);

        var failures = await dispatcher.DispatchAsync(CreateInvocation());

        var failure = Assert.Single(failures);
        Assert.Equal("failed", failure.RuneName);
        Assert.Equal("The invocation could not be queued.", failure.Message);
        Assert.Equal(2, transport.Attempts.Count);
    }

    private static MessageCreateEventRuneInvocation CreateInvocation() =>
        new(Guid.NewGuid(), 42, 100, 200, 300, "Ada", "hello");

    private static RegisteredRune CreateRune(
        string name,
        ulong guildId,
        RuneEventType eventType,
        bool enabled,
        BuiltRuneArtifact? artifact = null) =>
        new(
            Guid.NewGuid(), guildId, name, RuneLanguage.JavaScript,
            eventType, "message.reply('pong')", enabled,
            artifact ?? new BuiltRuneArtifact(
                $"artifact-{name}", "sha256:test", "rune", 1024));

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
