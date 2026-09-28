using Rune.Api;
using Rune.Api.Generator;

using Xunit;

namespace Rune.Api.Generator.Tests;

public sealed class RuneApiGeneratorTests
{
    private static readonly string Root =
        FindRepositoryRoot();

    [Fact]
    public void Generator_emits_only_the_selected_language_wrappers()
    {
        var output =
            RuneApiEmitter.Emit(Load());

        Assert.Equal(
            [
                "generated/rust/rune_api.rs",
                "generated/typescript/rune-api.ts"
            ],
            output.Keys);
    }

    [Fact]
    public void TypeScript_preserves_inheritance_and_event_argument_types()
    {
        var typescript =
            RuneApiEmitter.Emit(Load())[
                "generated/typescript/rune-api.ts"];

        Assert.Contains(
            "export class Message extends RestMessage",
            typescript);

        Assert.Contains(
            "readonly channelId: string",
            typescript);

        Assert.Contains(
            "MessageReactionAdd: MessageReactionAddEventArgs",
            typescript);

        Assert.Contains(
            "MessageReactionRemove: MessageReactionRemoveEventArgs",
            typescript);

        Assert.Contains(
            "async reply(replyMessage: ReplyMessageProperties)",
            typescript);

        Assert.Contains(
            "\"message.reply\"",
            typescript);

        Assert.Contains(
            "\"NetCord.Rest.RestMessage.ReplyAsync\"",
            typescript);

        Assert.Contains(
            "REST_MESSAGE_REPLY",
            typescript);

        Assert.Contains(
            "export class RuneHost",
            typescript);

        Assert.Contains(
            "call(method: string, payload: any)",
            typescript);

        Assert.DoesNotContain(
            "RuneHost | undefined",
            typescript);
    }

    [Fact]
    public void Rust_flattens_inherited_fields_and_uses_idiomatic_names()
    {
        var rust =
            RuneApiEmitter.Emit(Load())[
                "generated/rust/rune_api.rs"];

        Assert.Contains(
            "pub struct Message",
            rust);

        Assert.Contains(
            "pub channel_id: u64",
            rust);

        Assert.Contains(
            "pub author: User",
            rust);

        Assert.Contains(
            "pub type_: ReactionType",
            rust);

        Assert.Contains(
            "pub enum RuneEventArguments",
            rust);

        Assert.Contains(
            "pub fn reply(",
            rust);

        Assert.Contains(
            "fn message_reply(",
            rust);

        Assert.Contains(
            "\"message.reply\"",
            rust);

        Assert.Contains(
            "\"NetCord.Rest.RestMessage.ReplyAsync\"",
            rust);

        Assert.Contains(
            "REST_MESSAGE_REPLY_NETCORD",
            rust);
    }

    [Fact]
    public void Generation_is_deterministic()
    {
        var model = Load();
        var first =
            RuneApiEmitter.Emit(model);
        var second =
            RuneApiEmitter.Emit(model);

        Assert.Equal(
            first.Keys,
            second.Keys);

        foreach (var path in first.Keys)
        {
            Assert.Equal(
                first[path],
                second[path]);
        }
    }

    private static RuneApiModel Load() =>
        RuneApiLoader.Load(
            Path.Combine(
                Root,
                "api",
                "rune-api.yaml"));

    private static string FindRepositoryRoot()
    {
        var directory =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "Rune.slnx")))
            {
                return directory.FullName;
            }

            directory =
                directory.Parent;
        }

        throw new InvalidOperationException(
            "Rune repository root was not found.");
    }
}
