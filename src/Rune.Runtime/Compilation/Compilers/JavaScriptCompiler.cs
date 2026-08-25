using System.Text;
using Rune.Core.Runes;

namespace Rune.Runtime.Compilation.Compilers;

public sealed class JavaScriptRuneCompiler(
    RuneCompilationOptions options,
    CompilerProcessRunner processRunner,
    WasmPipeline wasmPipeline)
    : ILanguageCompiler
{
    public RuneLanguage Language => RuneLanguage.JavaScript;

    public async ValueTask<CompiledRune> CompileAsync(
        RuneEventType eventType,
        string source,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"rune-{Guid.NewGuid():N}");

        Directory.CreateDirectory(directory);

        try
        {
            var input = Path.Combine(directory, "rune.js");
            var output = Path.Combine(directory, "rune.wasm");

            await File.WriteAllTextAsync(
                input,
                await BuildWrapperAsync(
                    options.GeneratedApiRoot,
                    eventType,
                    source,
                    cancellationToken),
                Encoding.UTF8,
                cancellationToken);

            var result = await processRunner.RunAsync(
                options.JavaScriptCompiler,
                directory,
                [
                    "componentize",
                    input,
                    "--wit",
                    options.RuneApiWitPath,
                    "--world-name",
                    World(eventType),
                    "--out",
                    output,
                    "--disable=all"
                ],
                options.JavaScriptTimeout,
                cancellationToken);

            var diagnostics = new[]
            {
                result.StandardOutput,
                result.StandardError
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

            return await wasmPipeline.ProcessAsync(
                output,
                eventType,
                diagnostics,
                cancellationToken);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch
            {
            }
        }
    }

    private static async ValueTask<string> BuildWrapperAsync(
        string generatedApiRoot,
        RuneEventType eventType,
        string source,
        CancellationToken cancellationToken)
    {
        var parameter = eventType == RuneEventType.MessageCreate
            ? "message"
            : "args";
        var payload = eventType switch
        {
            RuneEventType.MessageCreate => "Message",
            RuneEventType.MessageDelete => "MessageDeleteEventArgs",
            RuneEventType.MessageReactionAdd => "MessageReactionAddEventArgs",
            RuneEventType.MessageReactionRemove => "MessageReactionRemoveEventArgs",
            _ => throw new ArgumentOutOfRangeException(
                nameof(eventType),
                eventType,
                "The gateway event is not supported.")
        };
        var facade = await File.ReadAllTextAsync(
            Path.Combine(
                generatedApiRoot,
                "javascript",
                "rune-api.js"),
            cancellationToken);

        return $$"""
{{facade}}

export function handle(value) {
    const {{parameter}} = new {{payload}}(value);
{{Indent(source, 4)}}
}
""";
    }

    private static string World(RuneEventType eventType) =>
        eventType switch
        {
            RuneEventType.MessageCreate => "message-create-rune",
            RuneEventType.MessageDelete => "message-delete-rune",
            RuneEventType.MessageReactionAdd => "message-reaction-add-rune",
            RuneEventType.MessageReactionRemove => "message-reaction-remove-rune",
            _ => throw new ArgumentOutOfRangeException(nameof(eventType))
        };

    private static string Indent(string source, int spaces)
    {
        var prefix = new string(' ', spaces);
        var body = string.IsNullOrWhiteSpace(source) ? "void 0;" : source;
        return string.Join(
            '\n',
            body.Replace("\r\n", "\n").Split('\n').Select(line => prefix + line));
    }
}
