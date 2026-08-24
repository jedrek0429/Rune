using Microsoft.Extensions.DependencyInjection;
using Rune.Runtime.Compilation.Compilers;

namespace Rune.Runtime.Compilation;

public static class DependencyInjection
{
    public static IServiceCollection AddRuneCompilation(
        this IServiceCollection services,
        Action<RuneCompilationOptions>? configure = null)
    {
        var options = new RuneCompilationOptions();

        configure?.Invoke(options);

        services
            .AddSingleton(options)
            .AddSingleton<CompilerProcessRunner>()
            .AddSingleton<WasmPipeline>()
            .AddSingleton<
                ILanguageCompiler,
                JavaScriptRuneCompiler>()
            .AddSingleton<
                ILanguageCompiler,
                PythonRuneCompiler>()
            .AddSingleton<
                ILanguageCompiler,
                RustRuneCompiler>()
            .AddSingleton<CompilerRegistry>();

        return services;
    }
}