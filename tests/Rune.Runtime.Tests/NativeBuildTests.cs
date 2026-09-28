using Rune.Core.Runes;
using Rune.Runtime;
using Xunit;

namespace Rune.Runtime.Tests;

public sealed class NativeBuildTests
{
    [Theory]
    [InlineData(RuneLanguage.Rust, "rust", "rust")]
    [InlineData(RuneLanguage.C, "clang", "c")]
    [InlineData(RuneLanguage.Cpp, "clang", "cpp")]
    public void Native_languages_map_to_isolated_build_profiles(
        RuneLanguage language,
        string expectedPool,
        string expectedLanguage)
    {
        var target = FirecrackerRuneBuilder.GetBuildTarget(language);

        Assert.Equal(expectedPool, target.Pool);
        Assert.Equal(expectedLanguage, target.Language);
    }

    [Fact]
    public void Build_boundary_returns_executable_artifacts()
    {
        Assert.True(typeof(IRuneBuilder).IsInterface);
        Assert.Equal("rune", new BuiltRuneArtifact("id", "digest", "rune", 1).Entrypoint);
    }
}
