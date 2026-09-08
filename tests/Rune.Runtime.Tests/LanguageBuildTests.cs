using Rune.Core.Runes;
using Rune.Runtime;
using Xunit;

namespace Rune.Runtime.Tests;

public sealed class LanguageBuildTests
{
    [Theory]
    [InlineData(RuneLanguage.JavaScript, "scriptc", "javascript")]
    [InlineData(RuneLanguage.TypeScript, "scriptc", "typescript")]
    [InlineData(RuneLanguage.Python, "python", "python")]
    [InlineData(RuneLanguage.Ruby, "ruby", "ruby")]
    [InlineData(RuneLanguage.CSharp, "dotnet-aot", "csharp")]
    public void Remaining_languages_map_to_isolated_build_profiles(
        RuneLanguage language,
        string expectedPool,
        string expectedLanguage)
    {
        var target = FirecrackerRuneBuilder.GetBuildTarget(language);

        Assert.Equal(expectedPool, target.Pool);
        Assert.Equal(expectedLanguage, target.Language);
    }
}
