using Rune.Api;

namespace Rune.Runtime;

public interface IRuneBuilder
{
    ValueTask<BuiltRuneArtifact> BuildAsync(
        RuneLanguage language,
        RuneApiEventType eventType,
        string source,
        CancellationToken cancellationToken = default);
}
