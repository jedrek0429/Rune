using Rune.Core.Runes;

namespace Rune.Core.Tests;

public sealed class BuiltRuneArtifactTests
{
    [Fact]
    public void RegisteredRune_can_carry_build_artifact_without_changing_source_language()
    {
        var artifact = new BuiltRuneArtifact(
            "sha256:abc",
            "sha256:abc",
            "rune",
            123);

        var rune = new RegisteredRune(
            Guid.NewGuid(),
            1,
            "hello",
            RuneLanguage.JavaScript,
            RuneEventType.MessageCreate,
            "export function handle() {}",
            [],
            true,
            artifact);

        Assert.Equal(RuneLanguage.JavaScript, rune.Language);
        Assert.Same(artifact, rune.Artifact);
        Assert.Equal("rune", rune.Artifact.Entrypoint);
    }
}
