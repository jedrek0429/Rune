using System.Text;
using System.Text.Json;
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
            var declarations = Path.Combine(directory, "rune.d.ts");
            var output = Path.Combine(directory, "rune.wasm");

            await File.WriteAllTextAsync(
                input,
                BuildWrapper(source),
                Encoding.UTF8,
                cancellationToken);

            await File.WriteAllTextAsync(
                declarations,
                Declarations,
                Encoding.UTF8,
                cancellationToken);

            var result = await processRunner.RunAsync(
                options.JavaScriptCompiler,
                directory,
                [input, "-i", declarations, "-o", output],
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
        var quotedSource = JsonSerializer.Serialize(source);

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

    private const string Declarations = """
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