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
            var declarations = Path.Combine(directory, "rune.d.ts");
            var output = Path.Combine(directory, "rune.wasm");

            await File.WriteAllTextAsync(
                input,
                BuildWrapper(eventType, source),
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

    private static string BuildWrapper(
        RuneEventType eventType,
        string source)
    {
        var quotedSource = JsonSerializer.Serialize(source);
        var parameter = eventType == RuneEventType.MessageCreate
            ? "message"
            : "args";
        var input = eventType switch
        {
            RuneEventType.MessageCreate =>
                "createMessage(invocation)",
            RuneEventType.MessageDelete =>
                "createMessageDeleteEventArgs(invocation)",
            RuneEventType.MessageReactionAdd =>
                "createMessageReactionAddEventArgs(invocation)",
            RuneEventType.MessageReactionRemove =>
                "createMessageReactionRemoveEventArgs(invocation)",
            _ => throw new ArgumentOutOfRangeException(
                nameof(eventType),
                eventType,
                "The gateway event is not supported.")
        };

        return $$"""
const AsyncFunction =
    Object.getPrototypeOf(async function () {}).constructor;

const __rune =
    new AsyncFunction("{{parameter}}", {{quotedSource}});

function createUser(data) {
    return {
        id: data.id,
        username: data.username
    };
}

function createMessage(data) {
    return {
        id: data.id,
        channelId: data.channelId,
        content: data.content,
        author: createUser(data.author)
    };
}

function createMessageDeleteEventArgs(data) {
    return {
        channelId: data.channelId,
        guildId: data.guildId,
        messageId: data.messageId
    };
}

function createMessageReactionEmoji(data) {
    return {
        animated: data.animated,
        id: data.id,
        name: data.name
    };
}

function createMessageReactionAddEventArgs(data) {
    return {
        burst: data.burst,
        channelId: data.channelId,
        emoji: createMessageReactionEmoji(data.emoji),
        guildId: data.guildId,
        messageAuthorId: data.messageAuthorId,
        messageId: data.messageId,
        type: data.type,
        userId: data.userId
    };
}

function createMessageReactionRemoveEventArgs(data) {
    return {
        burst: data.burst,
        channelId: data.channelId,
        emoji: createMessageReactionEmoji(data.emoji),
        guildId: data.guildId,
        messageId: data.messageId,
        type: data.type,
        userId: data.userId
    };
}

async function handle() {
    const invocation =
        JSON.parse(Host.inputString());

    await __rune({{input}});
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
""";
}
