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

    private static string BuildWrapper(
        RuneEventType eventType,
        string source)
    {
        var body = string.Join(
            '\n',
            source
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(line => $"    {line}"));

        if (string.IsNullOrWhiteSpace(source))
            body = "    pass";

        var parameter = eventType == RuneEventType.MessageCreate
            ? "message"
            : "args";
        var input = eventType switch
        {
            RuneEventType.MessageCreate =>
                "Message(invocation)",
            RuneEventType.MessageDelete =>
                "MessageDeleteEventArgs(invocation)",
            RuneEventType.MessageReactionAdd =>
                "MessageReactionAddEventArgs(invocation)",
            RuneEventType.MessageReactionRemove =>
                "MessageReactionRemoveEventArgs(invocation)",
            _ => throw new ArgumentOutOfRangeException(
                nameof(eventType),
                eventType,
                "The gateway event is not supported.")
        };

        return $$"""
import extism


class User:
    def __init__(self, data):
        self.id = data["id"]
        self.username = data["username"]


class Message:
    def __init__(self, data):
        self.id = data["id"]
        self.channel_id = data["channelId"]
        self.content = data["content"]
        self.author = User(data["author"])


class MessageDeleteEventArgs:
    def __init__(self, data):
        self.channel_id = data["channelId"]
        self.guild_id = data["guildId"]
        self.message_id = data["messageId"]


class MessageReactionEmoji:
    def __init__(self, data):
        self.animated = data["animated"]
        self.id = data["id"]
        self.name = data["name"]


class MessageReactionAddEventArgs:
    def __init__(self, data):
        self.burst = data["burst"]
        self.channel_id = data["channelId"]
        self.emoji = MessageReactionEmoji(data["emoji"])
        self.guild_id = data["guildId"]
        self.message_author_id = data["messageAuthorId"]
        self.message_id = data["messageId"]
        self.type = data["type"]
        self.user_id = data["userId"]


class MessageReactionRemoveEventArgs:
    def __init__(self, data):
        self.burst = data["burst"]
        self.channel_id = data["channelId"]
        self.emoji = MessageReactionEmoji(data["emoji"])
        self.guild_id = data["guildId"]
        self.message_id = data["messageId"]
        self.type = data["type"]
        self.user_id = data["userId"]


async def __rune__({{parameter}}):
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
            {{input}}
        )
    )
""";
    }
}
