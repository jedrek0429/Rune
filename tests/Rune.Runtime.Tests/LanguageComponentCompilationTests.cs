using Rune.Core.Runes;
using Rune.Runtime.Compilation;
using Rune.Runtime.Compilation.Compilers;
using Rune.Runtime.Exceptions;
using Xunit;

namespace Rune.Runtime.Tests;

public sealed class LanguageComponentCompilationTests
{
    private static readonly string Root = FindRepositoryRoot();

    public static TheoryData<RuneLanguage, RuneEventType> LanguageEvents =>
        new()
        {
            { RuneLanguage.JavaScript, RuneEventType.MessageCreate },
            { RuneLanguage.JavaScript, RuneEventType.MessageDelete },
            { RuneLanguage.JavaScript, RuneEventType.MessageReactionAdd },
            { RuneLanguage.JavaScript, RuneEventType.MessageReactionRemove },
            { RuneLanguage.Python, RuneEventType.MessageCreate },
            { RuneLanguage.Python, RuneEventType.MessageDelete },
            { RuneLanguage.Python, RuneEventType.MessageReactionAdd },
            { RuneLanguage.Python, RuneEventType.MessageReactionRemove },
            { RuneLanguage.Rust, RuneEventType.MessageCreate },
            { RuneLanguage.Rust, RuneEventType.MessageDelete },
            { RuneLanguage.Rust, RuneEventType.MessageReactionAdd },
            { RuneLanguage.Rust, RuneEventType.MessageReactionRemove }
        };

    [Theory]
    [MemberData(nameof(LanguageEvents))]
    public async Task Real_backend_compiles_selected_world_to_a_component(
        RuneLanguage language,
        RuneEventType eventType)
    {
        var compiler = CreateCompiler(language);

        var compiled = await compiler.CompileAsync(
            eventType,
            ValidSource(language, eventType));

        Assert.NotEmpty(compiled.Wasm);
        Assert.Equal([0x00, 0x61, 0x73, 0x6D], compiled.Wasm[..4]);
        Assert.Equal(eventType, compiled.EventType);
        Assert.Equal(
            "f8ed91c0031f87daf067eff1714365ba0b5d6a3636f784359768e42d59b677d9",
            compiled.ApiFingerprint);
    }

    [Theory]
    [InlineData(RuneLanguage.JavaScript)]
    [InlineData(RuneLanguage.Python)]
    [InlineData(RuneLanguage.Rust)]
    public async Task Invalid_source_has_sanitised_component_diagnostics(
        RuneLanguage language)
    {
        var compiler = CreateCompiler(language);

        var exception = await Assert.ThrowsAsync<RuneCompilationException>(
            async () =>
            {
                _ = await compiler.CompileAsync(
                    RuneEventType.MessageCreate,
                    InvalidSource(language));
            });

        Assert.DoesNotContain(Path.GetTempPath(), exception.Message);
        Assert.DoesNotContain("rune-", exception.Message);
    }

    [Theory]
    [InlineData(RuneLanguage.JavaScript, "rune.mjs")]
    [InlineData(RuneLanguage.Python, "rune.py")]
    [InlineData(RuneLanguage.Rust, "showcase.rs")]
    public async Task Message_create_example_compiles_to_a_component(
        RuneLanguage language,
        string fileName)
    {
        var compiler = CreateCompiler(language);
        var source = await File.ReadAllTextAsync(
            Path.Combine(Root, "examples", fileName));

        var compiled = await compiler.CompileAsync(
            RuneEventType.MessageCreate,
            source);

        Assert.NotEmpty(compiled.Wasm);
        Assert.Equal(RuneEventType.MessageCreate, compiled.EventType);
    }

    private static ILanguageCompiler CreateCompiler(RuneLanguage language)
    {
        var options = new RuneCompilationOptions
        {
            JavaScriptCompiler = Tool(
                "RUNE_JCO",
                Path.Combine(
                    Root,
                    "..",
                    ".toolchains",
                    "jco",
                    "node_modules",
                    ".bin",
                    "jco")),
            PythonCompiler = Tool(
                "RUNE_COMPONENTIZE_PY",
                Path.Combine(
                    Root,
                    "..",
                    ".toolchains",
                    "componentize-py",
                    "bin",
                    "componentize-py")),
            RustCompiler = Tool(
                "RUNE_CARGO",
                Path.Combine(
                    Root,
                    "..",
                    ".toolchains",
                    "rust",
                    "cargo",
                    "bin",
                    "cargo")),
            RuneApiWitPath = Path.Combine(Root, "wit", "rune-api.wit"),
            GeneratedApiRoot = Path.Combine(Root, "generated"),
            RustTargetDirectory = Path.Combine(
                Path.GetTempPath(),
                "rune-component-target")
        };
        var runner = new CompilerProcessRunner();
        var pipeline = new WasmPipeline();

        return language switch
        {
            RuneLanguage.JavaScript =>
                new JavaScriptRuneCompiler(options, runner, pipeline),
            RuneLanguage.Python =>
                new PythonRuneCompiler(options, runner, pipeline),
            RuneLanguage.Rust =>
                new RustRuneCompiler(options, runner, pipeline),
            _ => throw new ArgumentOutOfRangeException(nameof(language))
        };
    }

    private static string ValidSource(
        RuneLanguage language,
        RuneEventType eventType)
    {
        var parameter = eventType == RuneEventType.MessageCreate
            ? "message"
            : "args";

        return language switch
        {
            RuneLanguage.JavaScript => $"void {parameter};",
            RuneLanguage.Python => parameter,
            RuneLanguage.Rust =>
                $"pub fn rune({parameter}: {Payload(eventType)}) {{ " +
                $"let _ = {parameter}; }}",
            _ => throw new ArgumentOutOfRangeException(nameof(language))
        };
    }

    private static string InvalidSource(RuneLanguage language) =>
        language switch
        {
            RuneLanguage.JavaScript => "const = ;",
            RuneLanguage.Python => "this is not valid Python !!!",
            RuneLanguage.Rust => "pub fn rune( {",
            _ => throw new ArgumentOutOfRangeException(nameof(language))
        };

    private static string Payload(RuneEventType eventType) =>
        eventType switch
        {
            RuneEventType.MessageCreate => "Message",
            RuneEventType.MessageDelete => "MessageDeleteEventArgs",
            RuneEventType.MessageReactionAdd => "MessageReactionAddEventArgs",
            RuneEventType.MessageReactionRemove => "MessageReactionRemoveEventArgs",
            _ => throw new ArgumentOutOfRangeException(nameof(eventType))
        };

    private static string Tool(string variable, string fallback) =>
        Environment.GetEnvironmentVariable(variable) ?? fallback;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rune.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Rune repository root was not found.");
    }
}
