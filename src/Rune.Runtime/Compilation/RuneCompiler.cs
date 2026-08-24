using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

using Rune.Core.Runes;
using Rune.Runtime.Exceptions;

namespace Rune.Runtime.Compilation;

public sealed class RuneCompiler(
    RuneCompilerOptions options)
{
    public async ValueTask<byte[]> CompileAsync(
        RuneLanguage language,
        string source,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"rune-{Guid.NewGuid():N}");

        Directory.CreateDirectory(directory);

        try
        {
            return language switch
            {
                RuneLanguage.JavaScript =>
                    await CompileJavaScriptAsync(
                        directory,
                        source,
                        cancellationToken),

                RuneLanguage.Python =>
                    await CompilePythonAsync(
                        directory,
                        source,
                        cancellationToken),

                _ => throw new RuneCompilationException(
                    $"Language '{language}' cannot be compiled.")
            };
        }
        finally
        {
            try
            {
                Directory.Delete(
                    directory,
                    recursive: true);
            }
            catch
            {
            }
        }
    }

    private async ValueTask<byte[]> CompileJavaScriptAsync(
        string directory,
        string source,
        CancellationToken cancellationToken)
    {
        var input = Path.Combine(
            directory,
            "rune.js");

        var declarations = Path.Combine(
            directory,
            "rune.d.ts");

        var output = Path.Combine(
            directory,
            "rune.wasm");

        await File.WriteAllTextAsync(
            input,
            BuildJavaScriptWrapper(source),
            Encoding.UTF8,
            cancellationToken);

        await File.WriteAllTextAsync(
            declarations,
            JavaScriptDeclarations,
            Encoding.UTF8,
            cancellationToken);

        await RunCompilerAsync(
            options.JavaScriptCompiler,
            directory,
            [
                input,
                "-i",
                declarations,
                "-o",
                output
            ],
            cancellationToken);

        return await File.ReadAllBytesAsync(
            output,
            cancellationToken);
    }

    private async ValueTask<byte[]> CompilePythonAsync(
        string directory,
        string source,
        CancellationToken cancellationToken)
    {
        var input = Path.Combine(
            directory,
            "rune.py");

        var output = Path.Combine(
            directory,
            "rune.wasm");

        await File.WriteAllTextAsync(
            input,
            BuildPythonWrapper(source),
            Encoding.UTF8,
            cancellationToken);

        await RunCompilerAsync(
            options.PythonCompiler,
            directory,
            [
                input,
                "-o",
                output
            ],
            cancellationToken);

        return await File.ReadAllBytesAsync(
            output,
            cancellationToken);
    }

    private async ValueTask RunCompilerAsync(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        Process process;

        try
        {
            process = Process.Start(startInfo)
                ?? throw new RuneCompilationException(
                    $"Could not start '{executable}'.");
        }
        catch (Win32Exception exception)
        {
            throw new RuneCompilationException(
                $"Rune compiler '{executable}' is not installed or is not on PATH.",
                exception);
        }

        using (process)
        {
            var stdoutTask =
                process.StandardOutput.ReadToEndAsync();

            var stderrTask =
                process.StandardError.ReadToEndAsync();

            using var timeout =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            timeout.CancelAfter(options.Timeout);

            try
            {
                await process.WaitForExitAsync(
                    timeout.Token);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    process.Kill(
                        entireProcessTree: true);
                }
                catch
                {
                }

                throw new RuneCompilationException(
                    "Rune compilation timed out.");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                var error =
                    string.IsNullOrWhiteSpace(stderr)
                        ? stdout
                        : stderr;

                throw new RuneCompilationException(
                    SanitiseCompilerError(
                        error,
                        workingDirectory));
            }
        }
    }

    private static string BuildJavaScriptWrapper(
        string source)
    {
        // Treat user source as data here. It cannot structurally
        // escape our wrapper.
        var quotedSource =
            JsonSerializer.Serialize(source);

        return $$"""
const AsyncFunction =
    Object.getPrototypeOf(async function () {}).constructor;

const __rune =
    new AsyncFunction("message", {{quotedSource}});

function createMessage(data) {
    return {
        id: data.id,
        channelId: data.channelId,
        content: data.content,

        author: {
            id: data.author.id,
            username: data.author.username
        },

        async reply(content) {
            const { rune_message_reply } =
                Host.getFunctions();

            const memory =
                Memory.fromString(String(content));

            rune_message_reply(memory.offset);
        }
    };
}

async function handle() {
    const invocation =
        JSON.parse(Host.inputString());

    await __rune(
        createMessage(invocation.message));
}

module.exports = {
    handle
};
""";
    }

    private static string BuildPythonWrapper(
        string source)
    {
        var normalised =
            source.Replace("\r\n", "\n");

        var body =
            string.Join(
                '\n',
                normalised
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

    private static string SanitiseCompilerError(
        string error,
        string temporaryDirectory)
    {
        var cleaned =
            error.Replace(
                temporaryDirectory,
                "<rune>",
                StringComparison.Ordinal);

        var lines = cleaned
            .Replace("\r\n", "\n")
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(8);

        var result =
            string.Join('\n', lines).Trim();

        if (string.IsNullOrWhiteSpace(result))
            return "Rune compilation failed.";

        return result.Length <= 1200
            ? result
            : result[..1200] + "…";
    }

    private const string JavaScriptDeclarations = """
declare module "main" {
    export function handle(): I32;
}

declare module "extism:host" {
    interface user {
        rune_message_reply(ptr: I64): void;
    }
}
""";
}
