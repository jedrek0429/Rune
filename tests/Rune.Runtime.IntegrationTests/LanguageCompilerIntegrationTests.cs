using Microsoft.Extensions.DependencyInjection;
using Rune.Core.Runes;
using Rune.Runtime;
using Rune.Runtime.Compilation;
using Rune.Runtime.Exceptions;
using Rune.Runtime.Wasm;
using Xunit;

namespace Rune.Runtime.IntegrationTests;

public sealed class LanguageCompilerIntegrationTests
{
    [Theory]
    [InlineData(
        RuneLanguage.JavaScript,
        "await message.reply(`hello ${message.author.username}`);")]
    [InlineData(
        RuneLanguage.Python,
        "await message.reply(f\"hello {message.author.username}\")")]
    [InlineData(
        RuneLanguage.Rust,
        """
        fn rune(message: RuneMessage) -> FnResult<()> {
            message.reply(format!("hello {}", message.author.username))
        }
        """)]
    public async Task Compiler_produces_a_valid_rune_with_handle(
        RuneLanguage language,
        string source)
    {
        using var services = CreateServices();
        var compiler = GetCompiler(services, language);

        var compiled = await compiler.CompileAsync(source);

        Assert.NotEmpty(compiled.Wasm);

        var validator = services.GetRequiredService<RuneWasmCache>();
        validator.Validate(compiled.Wasm);
    }

    [Theory]
    [InlineData(RuneLanguage.JavaScript, "await message.reply(")]
    [InlineData(RuneLanguage.Python, "await message.reply(")]
    [InlineData(
        RuneLanguage.Rust,
        "fn rune(message: RuneMessage) -> FnResult<()> { message.reply(")]
    public async Task Invalid_source_has_sanitised_diagnostics(
        RuneLanguage language,
        string source)
    {
        using var services = CreateServices();
        var compiler = GetCompiler(services, language);

        var exception = await Assert.ThrowsAsync<RuneCompilationException>(
            async () => await compiler.CompileAsync(source));

        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
        Assert.DoesNotMatch(
            @"[/\\]rune-[0-9a-f]{32}",
            exception.Message);
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();

        services.AddRuneCompilation();
        services.AddRuneRuntime();

        return services.BuildServiceProvider();
    }

    private static ILanguageCompiler GetCompiler(
        IServiceProvider services,
        RuneLanguage language)
    {
        var compiler = services
            .GetServices<ILanguageCompiler>()
            .Single(candidate => candidate.Language == language);

        return compiler;
    }
}
