using Rune.Api;

namespace Rune.Runtime;

public static class RuneSourceComposer
{
    public static string Compose(
        RuneLanguage language,
        RuneApiEventType eventType,
        string source,
        string typeScriptDeclarations,
        string javaScriptRuntime,
        string rustBinding) =>
        language switch
        {
            RuneLanguage.TypeScript =>
                ComposeTypeScript(
                    eventType,
                    source,
                    typeScriptDeclarations,
                    javaScriptRuntime),

            RuneLanguage.Rust =>
                ComposeRust(
                    eventType,
                    source,
                    rustBinding),

            _ => source
        };

    private static string ComposeTypeScript(
        RuneApiEventType eventType,
        string source,
        string declarations,
        string runtime)
    {
        var implementation =
            string.IsNullOrWhiteSpace(runtime)
                ? declarations
                : runtime;

        if (string.IsNullOrWhiteSpace(implementation))
        {
            throw new InvalidOperationException(
                "Generated TypeScript Rune API binding is missing. Run Rune.Generator before building runes.");
        }

        var (payloadType, argument) =
            Event(eventType);

        return
            """
import { readFileSync } from "node:fs";

""" +
            $"async function rune({argument}: {payloadType}, host: RuneHost): Promise<void> {{\n" +
            "// <rune-user-source>\n" +
            source +
            "\n// </rune-user-source>\n" +
            "}\n\n" +
            implementation +
            """

""" +
            $$"""
async function __runeMain(): Promise<void> {
    const envelope = JSON.parse(readFileSync(0, "utf8")) as {
        payload: any;
    };

    const actions: Array<{ method: string; arguments: unknown }> = [];
    const host = new RuneHost(
        async (method: string, payload: any): Promise<any> => {
            actions.push({ method, arguments: payload });

            if (
                typeof payload === "object" &&
                payload !== null &&
                "replyMessage" in payload
            ) {
                return (payload as { replyMessage: any }).replyMessage;
            }

            return {};
        },
    );

    const {{argument}} = new {{payloadType}}(envelope.payload, host);

    try {
        await rune({{argument}}, host);
        console.log(JSON.stringify({ actions, error: null }));
    } catch (error) {
        console.log(JSON.stringify({
            actions: [],
            error: error instanceof Error ? error.message : String(error),
        }));
    }
}

void __runeMain();
""";
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
            $"fn rune({argument}: {payloadType}, host: &mut dyn RuneHost) -> Result<(), String> {{\n" +
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

struct RuntimeRuneHost {
    actions: Vec<RuntimeRuneHostAction>,
}

impl RuneHost for RuntimeRuneHost {
    fn message_reply(
        &mut self,
        reply_message: &ReplyMessageProperties,
    ) -> Result<RestMessage, String> {
        self.actions.push(RuntimeRuneHostAction {
            method: "message.reply".to_string(),
            arguments: serde_json::json!({
                "replyMessage": reply_message,
            }),
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

    let mut host =
        RuntimeRuneHost { actions: Vec::new() };

    let error =
        rune({{argument}}, &mut host).err();

    println!(
        "{}",
        serde_json::json!({
            "actions": host.actions,
            "error": error,
        })
    );
}
""";
    }

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
