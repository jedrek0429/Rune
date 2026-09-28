using System.Text.Json;

using Rune.Core.Invocations;
using Rune.Core.Runes;
using Rune.Runtime;

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

        Assert.Null(
            typeof(RegisteredRune)
                .Assembly
                .GetType(
                    "Rune.Core.Runes.CompiledRune"));
    }

    [Fact]
    public async Task Dispatcher_queues_artifact_only_envelopes_for_all_supported_events()
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

            AssertPayload(
                invocation.EventType,
                envelope.Payload);
        }
    }

    private static IEnumerable<
        EventRuneInvocation>
        Invocations()
    {
        yield return
            new MessageCreateEventRuneInvocation(
                Guid.NewGuid(),
                1,
                2,
                3,
                4,
                "user",
                "hello");

        yield return
            new MessageDeleteEventRuneInvocation(
                Guid.NewGuid(),
                1,
                2,
                3);

        yield return
            new MessageReactionAddEventRuneInvocation(
                Guid.NewGuid(),
                1,
                2,
                3,
                4,
                5,
                new MessageReactionEmojiInvocation(
                    false,
                    6,
                    "x"),
                false,
                0);

        yield return
            new MessageReactionRemoveEventRuneInvocation(
                Guid.NewGuid(),
                1,
                2,
                3,
                4,
                new MessageReactionEmojiInvocation(
                    false,
                    6,
                    "x"),
                false,
                0);
    }

    private static void AssertPayload(
        RuneEventType eventType,
        JsonElement payload)
    {
        switch (eventType)
        {
            case RuneEventType.MessageCreate:
                Assert.Equal(
                    "3",
                    payload
                        .GetProperty("id")
                        .GetString());

                Assert.Equal(
                    "4",
                    payload
                        .GetProperty("author")
                        .GetProperty("id")
                        .GetString());

                break;

            case RuneEventType.MessageDelete:
                Assert.Equal(
                    "3",
                    payload
                        .GetProperty("messageId")
                        .GetString());

                break;

            case RuneEventType.MessageReactionAdd:
                Assert.Equal(
                    "5",
                    payload
                        .GetProperty("messageAuthorId")
                        .GetString());

                Assert.Equal(
                    "6",
                    payload
                        .GetProperty("emoji")
                        .GetProperty("id")
                        .GetString());

                break;

            case RuneEventType.MessageReactionRemove:
                Assert.Equal(
                    "4",
                    payload
                        .GetProperty("userId")
                        .GetString());

                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(eventType));
        }
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
