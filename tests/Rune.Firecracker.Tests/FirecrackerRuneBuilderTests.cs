using System.Runtime.Versioning;
using Rune.Core.Runes;
using Rune.Runtime;

namespace Rune.Firecracker.Tests;

public sealed class FirecrackerRuneBuilderTests
{
    [Theory]
    [InlineData(RuneLanguage.JavaScript, "scriptc", "javascript")]
    [InlineData(RuneLanguage.TypeScript, "scriptc", "typescript")]
    [InlineData(RuneLanguage.Rust, "rust", "rust")]
    [InlineData(RuneLanguage.C, "clang", "c")]
    [InlineData(RuneLanguage.Cpp, "clang", "cpp")]
    [InlineData(RuneLanguage.CSharp, "dotnet-aot", "csharp")]
    [InlineData(RuneLanguage.Python, "python", "python")]
    [InlineData(RuneLanguage.Ruby, "ruby", "ruby")]
    public void EveryLanguageHasOneBuildTarget(
        RuneLanguage language,
        string pool,
        string wireLanguage)
    {
        Assert.Equal((pool, wireLanguage), FirecrackerRuneBuilder.GetBuildTarget(language));
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public async Task BuilderParsesOpaqueArtifactDescriptor()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rune-build-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "build.sh");
        await File.WriteAllTextAsync(script, "echo 'sha256:abc 123 rune'\n");

        try
        {
            var artifact = await new FirecrackerRuneBuilder(script)
                .BuildAsync(RuneLanguage.Rust, "fn main() {}");

            Assert.Equal(new BuiltRuneArtifact("sha256:abc", "sha256:abc", "rune", 123), artifact);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
