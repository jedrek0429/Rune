using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

using Rune.Core.Runes;
using Rune.Runtime.Exceptions;

namespace Rune.Runtime.Compilation;

public sealed class CompilerRegistry(
    IEnumerable<ILanguageCompiler> compilers)
{
    private readonly IReadOnlyDictionary<
        RuneLanguage,
        ILanguageCompiler> _compilers = compilers.ToDictionary(
            compiler => compiler.Language);

    public ILanguageCompiler Get(
        RuneLanguage language)
    {
        if (!_compilers.TryGetValue(
            language,
            out var compiler))
        {
            throw new RuneCompilationException(
                $"Language '{language}' cannot be compiled.");
        }

        return compiler;
    }
}
