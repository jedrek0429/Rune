using System.Text.Json;
using Rune.Core.Invocations;
using Rune.Core.Runes;
using Rune.Runtime;
using Xunit;

namespace Rune.Runtime.Tests;

public sealed class RuntimeCutoverTests
{
    [Fact]
    public async Task Registration_builds_and_stores_executable_artifact()
    {
        var registry = new RuneRegistry();
        var artifact = new BuiltRuneArtifact("sha256:a", "sha256:a", "rune", 1);
        var service = new RuneService(registry, new FakeBuilder(artifact));

        var rune = await service.RegisterAsync(
            1,
            "hello",
            RuneLanguage.Rust,
            "fn main() {}",
            CancellationToken.None);

        Assert.Equal(artifact, rune.Artifact);
        Assert.Null(typeof(RegisteredRune).GetProperty("Wasm"));
    }

    [Fact]
    public async Task Dispatch_queues_artifact_only_envelope()
    {
        var registry = new RuneRegistry();
        var artifact = new BuiltRuneArtifact("sha256:a", "sha256:a", "rune", 1);
        registry.Add(new RegisteredRune(
            Guid.NewGuid(),
            1,
            "hello",
            RuneLanguage.Rust,
            RuneEventType.MessageCreate,
            "source",
            true,
            artifact));
        var transport = new FakeTransport();
        var dispatcher = new RuneEventDispatcher(registry, transport);
        var invocation = new MessageCreateEventRuneInvocation(
            Guid.NewGuid(), 1, 2, 3, 4, "user", "hello");

        var failures = await dispatcher.DispatchAsync(invocation);

        Assert.Empty(failures);
        var envelope = Assert.Single(transport.Envelopes);
        Assert.Equal(artifact, envelope.Artifact);
        var json = JsonSerializer.Serialize(envelope);
        Assert.DoesNotContain("language", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("source", json, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeBuilder(BuiltRuneArtifact artifact) : IRuneBuilder
    {
        public ValueTask<BuiltRuneArtifact> BuildAsync(
            RuneLanguage language,
            string source,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(artifact);
    }

    private sealed class FakeTransport : IRuneTransport
    {
        public List<RuneInvocationEnvelope> Envelopes { get; } = [];

        public ValueTask EnqueueAsync(
            RuneInvocationEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Envelopes.Add(envelope);
            return ValueTask.CompletedTask;
        }
    }
}
