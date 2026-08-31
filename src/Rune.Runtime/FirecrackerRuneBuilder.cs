using Rune.Core.Runes;

namespace Rune.Runtime;

public sealed class FirecrackerRuneBuilder : IRuneBuilder
{
    public static (string Pool, string Language) GetBuildTarget(RuneLanguage language) =>
        language switch
        {
            RuneLanguage.Rust => ("rust", "rust"),
            RuneLanguage.C => ("clang", "c"),
            RuneLanguage.Cpp => ("clang", "cpp"),
            _ => throw new ArgumentOutOfRangeException(nameof(language))
        };

    public ValueTask<BuiltRuneArtifact> BuildAsync(
        RuneLanguage language,
        string source,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Firecracker build VMs are not wired yet.");
}
