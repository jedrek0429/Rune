using Rune.Core.Runes;

namespace Rune.Runtime;

public interface IRuneBuilder
{
    ValueTask<BuiltRuneArtifact> BuildAsync(
        RuneLanguage language,
        string source,
        CancellationToken cancellationToken = default);
}
