using System.Text.Json;

using Rune.Core.Invocations;
using Rune.Core.Runes;
using Xunit;

namespace Rune.Core.Tests;

public sealed class RuneInvocationEnvelopeTests
{
    [Fact]
    public void Serialized_envelope_contains_artifact_but_not_source_language_or_source()
    {
        var payload = JsonSerializer.SerializeToElement(new { message = new { id = 1 } });
        var envelope = new RuneInvocationEnvelope(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "hello",
            42,
            RuneEventType.MessageCreate,
            new BuiltRuneArtifact("sha256:abc", "sha256:abc", "rune", 123),
            payload,
            DateTimeOffset.UnixEpoch);

        var json = JsonSerializer.Serialize(envelope);

        Assert.Contains("Artifact", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Language", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Source", json, StringComparison.Ordinal);
    }
}
