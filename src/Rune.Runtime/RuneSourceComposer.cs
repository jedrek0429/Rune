using Rune.Api;

namespace Rune.Runtime;

public static class RuneSourceComposer
{
    public static string Compose(
        RuneLanguage language,
        RuneApiEventType eventType,
        string source,
        string typeScriptBinding,
        string javaScriptBinding,
        string rustBinding) =>
        language switch
        {
            RuneLanguage.JavaScript =>
                ComposeJavaScript(
                    eventType,
                    source,
                    javaScriptBinding),

            RuneLanguage.TypeScript =>
                ComposeTypeScript(
                    eventType,
                    source,
                    typeScriptBinding),

            RuneLanguage.Rust =>
                ComposeRust(
                    eventType,
                    source,
                    rustBinding),

            _ => source
        };

    private static string ComposeJavaScript(
        RuneApiEventType eventType,
        string source,
        string binding)
    {
        if (string.IsNullOrWhiteSpace(binding))
        {
            throw new InvalidOperationException(
                "Generated JavaScript Rune API binding is missing. Run Rune.Generator before building runes.");
        }

        var (payloadType, argument) =
            Event(eventType);

        var payloadInputType =
            payloadType + "Input";

        return
            "import { readFileSync } from \"node:fs\";\n\n" +
            "/**\n" +
            $" * @param {{{payloadType}}} {argument}\n" +
            " * @param {RuneHost} host\n" +
            " */\n" +
            "async function rune(" +
            argument +
            ", host) {\n" +
            "// <rune-user-source>\n" +
            source +
            "\n// </rune-user-source>\n" +
            "}\n\n" +
            binding +
            "\n\n" +
            "/** @typedef {{ payload: " +
            payloadInputType +
            " }} __RuneEnvelope */\n" +
            "/** @typedef {{ replyMessage: ReplyMessagePropertiesInput }} __RuneReplyArguments */\n" +
            "/** @typedef {{ method: string, arguments: __RuneReplyArguments }} __RuneAction */\n\n" +
            "async function __runeMain() {\n" +
            "    const envelope = /** @type {__RuneEnvelope} */ (\n" +
            "        JSON.parse(readFileSync(0, \"utf8\"))\n" +
            "    );\n\n" +
            "    /** @type {Array<__RuneAction>} */\n" +
            "    const actions = [];\n" +
            "    const host = new RuneHost(\n" +
            "        async (replyMessage) => {\n" +
            "            actions.push({\n" +
            "                method: \"message.reply\",\n" +
            "                arguments: { replyMessage },\n" +
            "            });\n\n" +
            "            return new RestMessage({\n" +
            "                id: \"0\",\n" +
            "                channelId: \"0\",\n" +
            "                content: replyMessage.content ?? \"\",\n" +
            "                author: { id: \"0\", username: \"Rune\" },\n" +
            "            });\n" +
            "        },\n" +
            "    );\n\n" +
            "    const " +
            argument +
            " = new " +
            payloadType +
            "(envelope.payload, host);\n\n" +
            "    try {\n" +
            "        await rune(" +
            argument +
            ", host);\n" +
            "        console.log(JSON.stringify({ actions, error: null }));\n" +
            "    } catch {\n" +
            "        console.log(JSON.stringify({\n" +
            "            actions: [],\n" +
            "            error: \"Rune execution failed.\",\n" +
            "        }));\n" +
            "    }\n" +
            "}\n\n" +
            "void __runeMain();\n";
    }

    private static string ComposeTypeScript(
        RuneApiEventType eventType,
        string source,
        string implementation)
    {

        if (string.IsNullOrWhiteSpace(implementation))
        {
            throw new InvalidOperationException(
                "Generated TypeScript Rune API binding is missing. Run Rune.Generator before building runes.");
        }

        var (payloadType, argument) =
            Event(eventType);

        var payloadShape =
            TypeScriptPayloadShape(
                eventType);

        return
            "import { readFileSync } from \"node:fs\";\n\n" +
            "async function rune(" +
            argument +
            ": " +
            payloadType +
            ", host: RuneHost): Promise<void> {\n" +
            "// <rune-user-source>\n" +
            source +
            "\n// </rune-user-source>\n" +
            "}\n\n" +
            implementation +
            "\n\n" +
            "async function __runeMain(): Promise<void> {\n" +
            "    const envelope = JSON.parse(readFileSync(0, \"utf8\")) as {\n" +
            "        payload: " +
            payloadShape +
            ";\n" +
            "    };\n\n" +
            "    const actions: Array<{ method: string; arguments: { replyMessage: { content: string | null } } }> = [];\n" +
            "    const host = new RuneHost(\n" +
            "        async (replyMessage: { content: string | null }): Promise<RestMessage> => {\n" +
            "            actions.push({\n" +
            "                method: \"message.reply\",\n" +
            "                arguments: { replyMessage },\n" +
            "            });\n\n" +
            "            return new RestMessage({\n" +
            "                id: \"0\",\n" +
            "                channelId: \"0\",\n" +
            "                content: replyMessage.content ?? \"\",\n" +
            "                author: { id: \"0\", username: \"Rune\" },\n" +
            "            });\n" +
            "        },\n" +
            "    );\n\n" +
            "    const " +
            argument +
            " = new " +
            payloadType +
            "(envelope.payload, host);\n\n" +
            "    try {\n" +
            "        await rune(" +
            argument +
            ", host);\n" +
            "        console.log(JSON.stringify({ actions, error: null }));\n" +
            "    } catch (error) {\n" +
            "        console.log(JSON.stringify({\n" +
            "            actions: [],\n" +
            "            error: error instanceof Error ? error.message : String(error),\n" +
            "        }));\n" +
            "    }\n" +
            "}\n\n" +
            "void __runeMain();\n";
    }

    private static string ComposeRust(
        RuneApiEventType eventType,
        string source,
        string binding)
    {
        if (string.IsNullOrWhiteSpace(binding))
        {
            throw new InvalidOperationException(
                "Generated Rust Rune API binding is missing. Run Rune.Generator before building runes.");
        }

        var (payloadType, argument) =
            Event(eventType);

        return
            """
use std::io::Read;

""" +
            $"fn rune({argument}: {payloadType}) -> Result<(), String> {{\n" +
            "// <rune-user-source>\n" +
            source +
            "\n// </rune-user-source>\n" +
            "    Ok(())\n}\n\n" +
            binding +
            """

#[derive(serde::Serialize)]
#[serde(rename_all = "camelCase")]
struct RuntimeRuneHostAction {
    method: String,
    arguments: serde_json::Value,
}

std::thread_local! {
    static __RUNE_ACTIONS: std::cell::RefCell<Vec<RuntimeRuneHostAction>> =
        std::cell::RefCell::new(Vec::new());
}

struct RuntimeRuneHost;

impl RuneHost for RuntimeRuneHost {
    fn message_reply(
        &mut self,
        reply_message: &ReplyMessageProperties,
    ) -> Result<RestMessage, String> {
        __RUNE_ACTIONS.with(|actions| {
            actions.borrow_mut().push(RuntimeRuneHostAction {
                method: "message.reply".to_string(),
                arguments: serde_json::json!({
                    "replyMessage": reply_message,
                }),
            });
        });

        Ok(RestMessage {
            id: 0,
            channel_id: 0,
            content: reply_message.content.clone().unwrap_or_default(),
            author: User {
                id: 0,
                username: "Rune".to_string(),
            },
        })
    }
}

""" +
            $$"""
fn main() {
    let mut input = String::new();

    if let Err(error) =
        std::io::stdin().read_to_string(&mut input)
    {
        println!(
            "{}",
            serde_json::json!({
                "actions": [],
                "error": error.to_string(),
            })
        );
        return;
    }

    let envelope: serde_json::Value =
        match serde_json::from_str(&input) {
            Ok(value) => value,
            Err(error) => {
                println!(
                    "{}",
                    serde_json::json!({
                        "actions": [],
                        "error": error.to_string(),
                    })
                );
                return;
            }
        };

    let {{argument}}: {{payloadType}} =
        match serde_json::from_value(envelope["payload"].clone()) {
            Ok(value) => value,
            Err(error) => {
                println!(
                    "{}",
                    serde_json::json!({
                        "actions": [],
                        "error": error.to_string(),
                    })
                );
                return;
            }
        };

    __RUNE_ACTIONS.with(|actions| {
        actions.borrow_mut().clear();
    });

    __rune_install_host(
        Box::new(RuntimeRuneHost),
    );

    let error =
        rune({{argument}}).err();

    let actions =
        __RUNE_ACTIONS.with(|actions| {
            std::mem::take(
                &mut *actions.borrow_mut(),
            )
        });

    println!(
        "{}",
        serde_json::json!({
            "actions": actions,
            "error": error,
        })
    );
}
""";
    }

    private static string TypeScriptPayloadShape(
        RuneApiEventType eventType) =>
        eventType switch
        {
            RuneApiEventType.MessageCreate =>
                "{ id: string; channelId: string; content: string; author: { id: string; username: string } }",

            RuneApiEventType.MessageDelete =>
                "{ channelId: string; guildId: string | null; messageId: string }",

            RuneApiEventType.MessageReactionAdd =>
                "{ burst: boolean; channelId: string; emoji: { animated: boolean; id: string | null; name: string | null }; guildId: string | null; messageAuthorId: string | null; messageId: string; type: number; userId: string }",

            RuneApiEventType.MessageReactionRemove =>
                "{ burst: boolean; channelId: string; emoji: { animated: boolean; id: string | null; name: string | null }; guildId: string | null; messageId: string; type: number; userId: string }",

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(eventType),
                    eventType,
                    null)
        };

    private static (string PayloadType, string Argument)
        Event(RuneApiEventType eventType) =>
        eventType switch
        {
            RuneApiEventType.MessageCreate =>
                ("Message", "message"),

            RuneApiEventType.MessageDelete =>
                ("MessageDeleteEventArgs", "event"),

            RuneApiEventType.MessageReactionAdd =>
                ("MessageReactionAddEventArgs", "event"),

            RuneApiEventType.MessageReactionRemove =>
                ("MessageReactionRemoveEventArgs", "event"),

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(eventType),
                    eventType,
                    null)
        };
}
