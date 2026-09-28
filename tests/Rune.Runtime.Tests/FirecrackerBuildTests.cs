using Rune.Core.Runes;
using Rune.Runtime;

using Xunit;

namespace Rune.Runtime.Tests;

public sealed class FirecrackerBuildTests
{
    [Theory]
    [InlineData(RuneLanguage.JavaScript, "scriptc", "javascript")]
    [InlineData(RuneLanguage.TypeScript, "scriptc", "typescript")]
    [InlineData(RuneLanguage.Python, "python", "python")]
    [InlineData(RuneLanguage.Rust, "rust", "rust")]
    [InlineData(RuneLanguage.C, "clang", "c")]
    [InlineData(RuneLanguage.Cpp, "clang", "cpp")]
    public void Every_supported_language_maps_to_a_build_target(
        RuneLanguage language,
        string expectedPool,
        string expectedLanguage)
    {
        var target =
            FirecrackerRuneBuilder.GetBuildTarget(
                language);

        Assert.Equal(
            expectedPool,
            target.Pool);

        Assert.Equal(
            expectedLanguage,
            target.Language);
    }

    [Fact]
    public void Build_boundary_returns_executable_artifacts()
    {
        Assert.True(
            typeof(IRuneBuilder).IsInterface);

        Assert.Equal(
            "rune",
            new BuiltRuneArtifact(
                "id",
                "digest",
                "rune",
                1).Entrypoint);
    }
}
