using Rune.Generator;

using Xunit;

namespace Rune.Generator.Tests;

public sealed class RuneGeneratorTests
{
    private static readonly string Root =
        FindRepositoryRoot();

    [Fact]
    public void Api_generation_emits_internal_and_guest_language_projections()
    {
        var output =
            RuneApiEmitter.Emit(LoadApi());

        Assert.Contains(
            "src/Rune.Api/Generated/RuneApi.g.cs",
            output.Keys);

        Assert.Contains(
            "generated/rust/rune_api.rs",
            output.Keys);

        Assert.Contains(
            "generated/javascript/rune-api.js",
            output.Keys);

        Assert.Contains(
            "generated/typescript/rune-api.ts",
            output.Keys);

        Assert.DoesNotContain(
            "generated/javascript/rune-api.d.ts",
            output.Keys);
    }

    [Fact]
    public void Generated_CSharp_owns_projection_wire_shape_and_event_identity()
    {
        var csharp =
            RuneApiEmitter.Emit(LoadApi())[
                "src/Rune.Api/Generated/RuneApi.g.cs"];

        Assert.Contains(
            "public sealed record Message(",
            csharp);

        Assert.Contains(
            ": RestMessage(",
            csharp);

        Assert.Contains(
            "public static class NetCordRuneApi",
            csharp);

        Assert.Contains(
            "public static class RuneApiPayload",
            csharp);

        Assert.Contains(
            "public enum RuneApiEventType",
            csharp);

        Assert.Contains(
            "MessageReactionRemove,",
            csharp);

        Assert.Contains(
            "guildId = value.GuildId is ulong guildIdSnowflake ? Snowflake(guildIdSnowflake) : null",
            csharp);
    }

    [Fact]
    public void JavaScript_binding_is_static_and_untyped()
    {
        var javascript =
            RuneApiEmitter.Emit(LoadApi())[
                "generated/javascript/rune-api.js"];

        Assert.Contains(
            "// @ts-check",
            javascript);

        Assert.Contains(
            "class Message extends RestMessage",
            javascript);

        var messageClass =
            javascript[
                javascript.IndexOf(
                    "class Message extends RestMessage",
                    StringComparison.Ordinal)..];

        Assert.Contains(
            "async reply(replyMessage)",
            messageClass);

        Assert.Contains(
            "@typedef {",
            javascript);

        Assert.Contains(
            "} ReplyMessagePropertiesInput",
            javascript);

        Assert.Contains(
            "@param {ReplyMessagePropertiesInput} replyMessage",
            javascript);

        Assert.Contains(
            "const result = await __runeHostMessageReply(replyMessage);",
            javascript);

        Assert.DoesNotContain(
            "__runeInstallMessageReply",
            javascript);

        Assert.DoesNotContain(
            "this.__host",
            javascript);

        Assert.DoesNotContain(
            "interface RuneEventArguments",
            javascript);

        Assert.DoesNotContain(
            "export class",
            javascript);

        Assert.DoesNotContain(
            "readonly channelId:",
            javascript);

        Assert.DoesNotContain(
            "payload: any",
            javascript);
    }

    [Fact]
    public void TypeScript_preserves_inheritance_and_event_argument_types()
    {
        var typescript =
            RuneApiEmitter.Emit(LoadApi())[
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
            "async reply(replyMessage: { content: string | null })",
            typescript);

        Assert.DoesNotContain(
            "async reply(replyMessage: ReplyMessageProperties)",
            typescript);

        Assert.Contains(
            "readonly messageReply: (replyMessage: { content: string | null }) => Promise<RestMessage>",
            typescript);

        Assert.Contains(
            "await this.__host.messageReply(replyMessage)",
            typescript);

        Assert.DoesNotContain(
            "payload: any",
            typescript);

        Assert.DoesNotContain(
            "Promise<any>",
            typescript);
    }

    [Fact]
    public void Rust_wrapper_is_idiomatic()
    {
        var rust =
            RuneApiEmitter.Emit(LoadApi())[
                "generated/rust/rune_api.rs"];

        Assert.Contains(
            "pub struct Message",
            rust);

        Assert.Contains(
            "pub channel_id: u64",
            rust);

        Assert.Contains(
            "#[serde(with = \"rune_snowflake_serde\")]",
            rust);

        Assert.Contains(
            "serde_repr::Deserialize_repr",
            rust);

        Assert.Contains(
            "#[serde(rename_all = \"camelCase\")]",
            rust);

        Assert.Contains(
            "pub type_: ReactionType",
            rust);

        Assert.Contains(
            "pub fn reply(",
            rust);

        Assert.Contains(
            "pub fn __rune_install_host(",
            rust);

        Assert.Contains(
            "__rune_with_host(|host|",
            rust);

        Assert.DoesNotContain(
            "&self, host: &mut dyn RuneHost",
            rust);
    }

    [Fact]
    public void Runtime_protocol_is_generated_for_CSharp_Rust_and_guest()
    {
        var api = LoadApi();
        var runtime =
            RuntimeContractLoader.Load(
                Path.Combine(
                    Root,
                    "contracts",
                    "runtime.yaml"));

        var output =
            RuntimeContractEmitter.Emit(
                runtime,
                api);

        Assert.Equal(
            [
                "src/Rune.Firecracker.Guest/src/generated_runtime.rs",
                "src/Rune.Firecracker.Runner/src/generated_protocol.rs",
                "src/Rune.Firecracker/generated-runtime.sh",
                "src/Rune.Runtime/Generated/RuntimeProtocol.g.cs"
            ],
            output.Keys);

        var csharp =
            output[
                "src/Rune.Runtime/Generated/RuntimeProtocol.g.cs"];

        var rust =
            output[
                "src/Rune.Firecracker.Runner/src/generated_protocol.rs"];

        var shell =
            output[
                "src/Rune.Firecracker/generated-runtime.sh"];

        Assert.Contains(
            "public const string InvocationStream = \"rune:invocations\";",
            csharp);

        Assert.Contains(
            "public sealed record RuneInvocationEnvelope(",
            csharp);

        Assert.Contains(
            "pub const INVOCATION_STREAM: &str = \"rune:invocations\";",
            rust);

        Assert.Contains(
            "pub const MAX_INVOCATION_BYTES: usize = 131072;",
            rust);

        Assert.Contains(
            "RUNE_MAX_RESULT_BYTES=262144",
            shell);

        foreach (var runeEvent in api.Events)
        {
            Assert.Contains(
                $"    {runeEvent.Name},",
                rust);
        }
    }

    [Fact]
    public void Generation_is_deterministic()
    {
        var api = LoadApi();
        var runtime =
            RuntimeContractLoader.Load(
                Path.Combine(
                    Root,
                    "contracts",
                    "runtime.yaml"));

        AssertEqual(
            RuneApiEmitter.Emit(api),
            RuneApiEmitter.Emit(api));

        AssertEqual(
            RuntimeContractEmitter.Emit(
                runtime,
                api),
            RuntimeContractEmitter.Emit(
                runtime,
                api));
    }


    [Fact]
    public void Api_documentation_is_user_facing_and_links_to_NetCord()
    {
        var documentation =
            RuneApiDocumentationEmitter.Emit(LoadApi());

        Assert.Contains(
            "docs/src/content/docs/api/generated/index.mdx",
            documentation.Keys);

        Assert.Contains(
            "title: API Reference",
            documentation[
                "docs/src/content/docs/api/generated/index.mdx"]);

        var events =
            documentation[
                "docs/src/content/docs/api/generated/events.mdx"];

        Assert.Contains(
            "| Event | Description | Payload | Source |",
            events);

        Assert.Contains(
            "A new Discord message is created.",
            events);

        Assert.Contains(
            "docs/src/data/generated/api.json",
            documentation.Keys);

        Assert.Contains(
            "\"runeApi\": \"0.1.0\"",
            documentation[
                "docs/src/data/generated/api.json"]);

        var message =
            documentation[
                "docs/src/content/docs/api/generated/types/message.mdx"];

        Assert.Contains(
            "title: Class Message",
            message);

        Assert.Contains(
            "## Overview",
            message);

        Assert.Contains(
            "rune-api-type-marker",
            message);

        Assert.Contains(
            "export class Message extends RestMessage",
            message);

        Assert.Contains(
            "<TabItem label=\"JavaScript\">",
            message);

        Assert.Contains(
            "class Message extends RestMessage",
            message);

        Assert.Contains(
            "pub struct Message",
            message);

        Assert.Contains(
            "### Inheritance",
            message);

        Assert.Contains(
            "object ← [RestMessage](../restmessage/) ← **Message**",
            message);

        Assert.Contains(
            "class=\"netcord-source\"",
            message);

        Assert.Contains(
            "https://netcord.dev/docs/NetCord.Gateway.Message.html",
            message);

        Assert.Contains(
            "### Inherited members",
            message);

        Assert.Contains(
            "A message from a MessageCreate event.",
            message);

        var overview =
            message[
                message.IndexOf(
                    "## Overview",
                    StringComparison.Ordinal)..];

        Assert.True(
            overview.IndexOf(
                "export class Message extends RestMessage",
                StringComparison.Ordinal) <
            overview.IndexOf(
                "A message from a MessageCreate event.",
                StringComparison.Ordinal));

        Assert.True(
            overview.IndexOf(
                "export class Message extends RestMessage",
                StringComparison.Ordinal) <
            overview.IndexOf(
                "### Inheritance",
                StringComparison.Ordinal));

        var restMessage =
            documentation[
                "docs/src/content/docs/api/generated/types/restmessage.mdx"];

        Assert.Contains(
            "## Properties",
            restMessage);

        Assert.Contains(
            "## Methods",
            restMessage);

        Assert.Contains(
            "#### Property Value",
            restMessage);

        Assert.Contains(
            "[User](../user/)",
            restMessage);

        Assert.Contains(
            "#### Parameters",
            restMessage);

        Assert.Contains(
            "**`replyMessage`**",
            restMessage);

        Assert.Contains(
            "[ReplyMessageProperties](../replymessageproperties/)",
            restMessage);

        Assert.Contains(
            "#### Returns",
            restMessage);

        Assert.Contains(
            "Replies to this message and returns the created message.",
            restMessage);

        Assert.Contains(
            "#### Example",
            restMessage);

        Assert.Contains(
            "const reply = await message.reply",
            restMessage);

        Assert.Contains(
            "```js",
            restMessage);

        Assert.Contains(
            "let reply = message.reply",
            restMessage);

        Assert.Contains(
            "https://netcord.dev/docs/NetCord.Rest.RestMessage.html",
            restMessage);

        Assert.Contains(
            "https://netcord.dev/docs/NetCord.User.html",
            restMessage);

        Assert.Contains(
            "title=\"NetCord.Rest.RestMessage.ReplyAsync\"",
            restMessage);


        Assert.DoesNotContain(
            "Namespace",
            restMessage);

        Assert.DoesNotContain(
            "Assembly",
            restMessage);

        Assert.Contains(
            "<Tabs syncKey=\"language\">",
            restMessage);
    }

    [Fact]
    public void Bot_documentation_is_generated_from_command_metadata_and_xml_comments()
    {
        var documentation =
            RuneBotDocumentationEmitter.Emit(Root);

        Assert.Contains(
            "docs/src/content/docs/bot/generated/index.mdx",
            documentation.Keys);

        Assert.Contains(
            "title: Bot Reference",
            documentation[
                "docs/src/content/docs/bot/generated/index.mdx"]);

        var rune =
            documentation[
                "docs/src/content/docs/bot/generated/commands/rune.mdx"];

        Assert.Contains(
            "## /rune register",
            rune);

        Assert.Contains(
            "Uploads source code and registers it as a rune for this server.",
            rune);

        Assert.Contains(
            "**Required permission:** `Manage Server`",
            rune);

        Assert.Contains(
            "| `eventType` | `RuneApiEventType` | The Discord event that runs this rune. |",
            rune);

        Assert.Contains(
            "/rune register name:hello event:MessageCreate file:hello.ts",
            rune);
    }

    private static RuneApiModel LoadApi() =>
        RuneApiLoader.Load(
            Path.Combine(
                Root,
                "contracts",
                "rune-api.yaml"));

    private static void AssertEqual(
        IReadOnlyDictionary<string, string> first,
        IReadOnlyDictionary<string, string> second)
    {
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
