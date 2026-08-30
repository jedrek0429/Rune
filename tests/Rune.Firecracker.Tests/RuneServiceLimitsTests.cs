using Rune.Core.Runes;
using Rune.Runtime;

namespace Rune.Firecracker.Tests;

public sealed class RuneServiceLimitsTests
{
    [Fact]
    public async Task RegistrationRejectsSourceOver64KiB()
    {
        var service = new RuneService(new RuneRegistry());
        var source = new string('x', RuneResourceLimits.MaxSourceBytes + 1);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.RegisterAsync(
                42,
                "too-large",
                RuneLanguage.Rust,
                RuneEventType.MessageCreate,
                source));

        Assert.Equal("Rune source may not exceed 64 KiB.", exception.Message);
    }

    [Fact]
    public async Task UpdateRejectsSourceOver64KiBAndKeepsCurrentRune()
    {
        var registry = new RuneRegistry();
        var service = new RuneService(registry);
        var current = await service.RegisterAsync(
            42,
            "existing",
            RuneLanguage.Rust,
            RuneEventType.MessageCreate,
            "valid");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.UpdateAsync(
                current,
                RuneLanguage.Rust,
                new string('x', RuneResourceLimits.MaxSourceBytes + 1)));

        Assert.Equal("valid", registry.Get(42, "existing")!.Source);
    }

    [Fact]
    public async Task CompletingBuildRejectsOversizedArtifact()
    {
        var registry = new RuneRegistry();
        var service = new RuneService(registry);
        var current = await service.RegisterAsync(
            42,
            "built",
            RuneLanguage.CSharp,
            RuneEventType.MessageCreate,
            "source");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.CompleteBuild(
                current,
                new BuiltRuneArtifact(
                    "artifact",
                    "sha256:test",
                    "rune",
                    RuneResourceLimits.MaxArtifactBytes + 1L)));

        Assert.Equal("Built Rune artifact may not exceed 16 MiB.", exception.Message);
        Assert.Null(registry.Get(42, "built")!.Artifact);
    }

    [Fact]
    public async Task CompletingBuildStoresValidArtifact()
    {
        var registry = new RuneRegistry();
        var service = new RuneService(registry);
        var current = await service.RegisterAsync(
            42,
            "built",
            RuneLanguage.CSharp,
            RuneEventType.MessageCreate,
            "source");
        var artifact = new BuiltRuneArtifact("artifact", "sha256:test", "rune", 1024);

        var updated = service.CompleteBuild(current, artifact);

        Assert.Same(artifact, updated.Artifact);
        Assert.Equal(artifact, registry.Get(42, "built")!.Artifact);
    }
}
