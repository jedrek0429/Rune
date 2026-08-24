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
                BuildWrapper(source),
                Encoding.UTF8,
                cancellationToken);

            var result = await processRunner.RunAsync(
                options.PythonCompiler,
                directory,
                [input, "-o", output],
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

    private static string BuildWrapper(string source)
    {
        var body = string.Join(
            '\n',
            source
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(line => $"    {line}"));

        if (string.IsNullOrWhiteSpace(source))
            body = "    pass";

        return $$"""
import extism


@extism.import_fn(
    "extism:host/user",
    "rune_message_reply"
)
def _rune_message_reply(content: str):
    ...


class RuneUser:
    def __init__(self, data):
        self.id = data["id"]
        self.username = data["username"]


class RuneMessage:
    def __init__(self, data):
        self.id = data["id"]
        self.channel_id = data["channelId"]
        self.content = data["content"]
        self.author = RuneUser(data["author"])

    async def reply(self, content):
        _rune_message_reply(str(content))


async def __rune__(message):
{{body}}


def _run(coroutine):
    try:
        coroutine.send(None)
    except StopIteration:
        return

    coroutine.close()

    raise RuntimeError(
        "Rune suspended on an unsupported asynchronous operation"
    )


@extism.plugin_fn
def handle():
    invocation = extism.input_json()

    _run(
        __rune__(
            RuneMessage(invocation["message"])
        )
    )
""";
    }
}
