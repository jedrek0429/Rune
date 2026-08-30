using Rune.Core.Runes;
using Rune.Runtime;

namespace Rune.Firecracker.Tests;

public sealed class RuneServiceLimitsTests
{
    private static readonly BuiltRuneArtifact Artifact =
        new("sha256:test", "sha256:test", "rune", 1024);

    [Fact]
    public async Task RegistrationBuildsBeforePublishingRune()
    {
        var registry = new RuneRegistry();
        var builder = new FakeBuilder(Artifact);
        var service = new RuneService(registry, builder);

        var rune = await service.RegisterAsync(
            42, "built", RuneLanguage.CSharp,
            RuneEventType.MessageCreate, "source");

        Assert.Equal(Artifact, rune.Artifact);
        Assert.Same(rune, registry.Get(42, "built"));
        Assert.Equal((RuneLanguage.CSharp, "source"), builder.LastBuild);
    }

    [Fact]
    public async Task FailedBuildDoesNotRegisterRune()
    {
        var registry = new RuneRegistry();
        var service = new RuneService(registry, new FakeBuilder(error: new InvalidOperationException("bad build")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync(
            42, "broken", RuneLanguage.Rust,
            RuneEventType.MessageCreate, "source").AsTask());

        Assert.Null(registry.Get(42, "broken"));
    }

    [Fact]
    public async Task UpdateBuildsBeforeReplacingRune()
    {
        var registry = new RuneRegistry();
        var builder = new FakeBuilder(Artifact);
        var service = new RuneService(registry, builder);
        var current = await service.RegisterAsync(
            42, "existing", RuneLanguage.Rust,
            RuneEventType.MessageCreate, "old");
        builder.Result = Artifact with { Id = "sha256:new", Digest = "sha256:new" };

        var updated = await service.UpdateAsync(current, RuneLanguage.C, "new");

        Assert.Equal("new", updated.Source);
        Assert.Equal(RuneLanguage.C, updated.Language);
        Assert.Equal("sha256:new", updated.Artifact!.Id);
        Assert.Same(updated, registry.Get(42, "existing"));
    }

    [Fact]
    public async Task SourceLimitIsCheckedBeforeBuild()
    {
        var builder = new FakeBuilder(Artifact);
        var service = new RuneService(new RuneRegistry(), builder);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterAsync(
            42, "too-large", RuneLanguage.Rust, RuneEventType.MessageCreate,
            new string('x', RuneResourceLimits.MaxSourceBytes + 1)).AsTask());

        Assert.Null(builder.LastBuild);
    }

    private sealed class FakeBuilder(
        BuiltRuneArtifact? result = null,
        Exception? error = null) : IRuneBuilder
    {
        public BuiltRuneArtifact Result { get; set; } = result ?? Artifact;
        public (RuneLanguage Language, string Source)? LastBuild { get; private set; }

        public ValueTask<BuiltRuneArtifact> BuildAsync(
            RuneLanguage language,
            string source,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastBuild = (language, source);
            return error is null
                ? ValueTask.FromResult(Result)
                : ValueTask.FromException<BuiltRuneArtifact>(error);
        }
    }
}
