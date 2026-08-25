using System.Text;
using Rune.Core.Runes;

namespace Rune.Runtime.Compilation.Compilers;

public sealed class PythonRuneCompiler(
    RuneCompilationOptions options,
    CompilerProcessRunner processRunner,
    WasmPipeline wasmPipeline)
    : ILanguageCompiler
{
    public RuneLanguage Language => RuneLanguage.Python;

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
            var input = Path.Combine(directory, "rune.py");
            var output = Path.Combine(directory, "rune.wasm");

            await File.WriteAllTextAsync(
                input,
                BuildWrapper(eventType, source),
                Encoding.UTF8,
                cancellationToken);

            var result = await processRunner.RunAsync(
                options.PythonCompiler,
                directory,
                [
                    "-d",
                    options.RuneApiWitPath,
                    "-w",
                    World(eventType),
                    "componentize",
                    "--stub-wasi",
                    "rune",
                    "-o",
                    output
                ],
                options.PythonTimeout,
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

    private static string BuildWrapper(
        RuneEventType eventType,
        string source)
    {
        var body = string.Join(
            '\n',
            source
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(line => $"        {line}"));

        if (string.IsNullOrWhiteSpace(source))
            body = "        pass";

        var parameter = eventType == RuneEventType.MessageCreate
            ? "message"
            : "args";
        return $$"""
import wit_world


class WitWorld(wit_world.WitWorld):
    def handle(self, {{parameter}}):
{{body}}
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
}
