using Rune.Core.Runes;

namespace Rune.Runtime.Compilation;

public interface ILanguageCompiler
{
    RuneLanguage Language { get; }

    ValueTask<CompiledRune> CompileAsync(
        string source,
        CancellationToken cancellationToken = default);
}

