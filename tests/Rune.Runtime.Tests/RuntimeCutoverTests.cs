using System.Text.Json;

using Rune.Api;

using Xunit;

namespace Rune.Runtime.Tests;

public sealed class RuntimeCutoverTests
{
    [Fact]
    public async Task Registration_builds_and_stores_only_an_executable_artifact()
    {
        var artifact =
            new BuiltRuneArtifact(
                "sha256:a",
                "sha256:a",
                "rune",
                1);

        var service =
            new RuneService(
                new RuneRegistry(),
                new FakeBuilder(artifact));

        var rune =
            await service.RegisterAsync(
                1,
                "hello",
                RuneLanguage.Rust,
                "fn main() {}");

        Assert.Equal(
            artifact,
            rune.Artifact);

        Assert.Null(
            typeof(RegisteredRune)
                .GetProperty("Wasm"));
    }

    [Fact]
    public async Task Dispatcher_passes_projected_payloads_into_artifact_only_envelopes()
    {
        foreach (var invocation in Invocations())
        {
            var registry =
                new RuneRegistry();

            var artifact =
                new BuiltRuneArtifact(
                    "sha256:a",
                    "sha256:a",
                    "rune",
                    1);

            registry.Add(
                new RegisteredRune(
                    Guid.NewGuid(),
                    invocation.GuildId,
                    invocation.EventType.ToString(),
                    RuneLanguage.Rust,
                    invocation.EventType,
                    "source",
                    true,
                    artifact));

            var transport =
                new FakeTransport();

            var dispatcher =
                new RuneEventDispatcher(
                    registry,
                    transport);

            var result =
                await dispatcher.DispatchAsync(
                    invocation);

            Assert.Empty(
                result.Failures);

            Assert.Equal(
                1,
                result.QueuedExecutions);

            var envelope =
                Assert.Single(
                    transport.Envelopes);

            Assert.Equal(
                artifact,
                envelope.Artifact);

            Assert.Equal(
                invocation.EventType,
                envelope.EventType);

            Assert.Equal(
                invocation.Payload.GetRawText(),
                envelope.Payload.GetRawText());

            var json =
                JsonSerializer.Serialize(
                    envelope);

            Assert.DoesNotContain(
                "language",
                json,
                StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(
                "source",
                json,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private static IEnumerable<EventRuneInvocation>
        Invocations()
    {
        yield return
            new EventRuneInvocation(
                Guid.NewGuid(),
                1,
                RuneApiEventType.MessageCreate,
                RuneApiPayload.Serialize(
                    new Message(
                        3,
                        2,
                        "hello",
                        new User(
                            4,
                            "user"))));

        yield return
            new EventRuneInvocation(
                Guid.NewGuid(),
                1,
                RuneApiEventType.MessageDelete,
                RuneApiPayload.Serialize(
                    new MessageDeleteEventArgs(
                        2,
                        1,
                        3)));

        yield return
            new EventRuneInvocation(
                Guid.NewGuid(),
                1,
                RuneApiEventType.MessageReactionAdd,
                RuneApiPayload.Serialize(
                    new MessageReactionAddEventArgs(
                        false,
                        2,
                        new MessageReactionEmoji(
                            false,
                            6,
                            "x"),
                        1,
                        5,
                        3,
                        ReactionType.Normal,
                        4)));

        yield return
            new EventRuneInvocation(
                Guid.NewGuid(),
                1,
                RuneApiEventType.MessageReactionRemove,
                RuneApiPayload.Serialize(
                    new MessageReactionRemoveEventArgs(
                        false,
                        2,
                        new MessageReactionEmoji(
                            false,
                            6,
                            "x"),
                        1,
                        3,
                        ReactionType.Normal,
                        4)));
    }

    private sealed class FakeBuilder(
        BuiltRuneArtifact artifact)
        : IRuneBuilder
    {
        public ValueTask<BuiltRuneArtifact>
            BuildAsync(
                RuneLanguage language,
                string source,
                CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                artifact);
    }

    private sealed class FakeTransport
        : IRuneTransport
    {
        public List<RuneInvocationEnvelope>
            Envelopes { get; } = [];

        public ValueTask EnqueueAsync(
            RuneInvocationEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Envelopes.Add(envelope);
            return ValueTask.CompletedTask;
        }

        public ValueTask<
            IReadOnlyList<RuneResultMessage>>
            ReadResultsAsync(
                string consumerName,
                CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<
                IReadOnlyList<RuneResultMessage>>(
                []);

        public ValueTask
            AcknowledgeResultAsync(
                string streamId,
                CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
