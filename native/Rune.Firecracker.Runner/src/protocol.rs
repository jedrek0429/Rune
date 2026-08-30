use serde::{Deserialize, Serialize};
use serde_json::Value;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub enum RuneLanguage {
    #[serde(rename = "javaScript")]
    Javascript,
    #[serde(rename = "python")]
    Python,
    #[serde(rename = "rust")]
    Rust,
}

impl RuneLanguage {
    pub const ALL: [Self; 3] = [Self::Javascript, Self::Python, Self::Rust];

    pub const fn as_str(self) -> &'static str {
        match self {
            Self::Javascript => "javascript",
            Self::Python => "python",
            Self::Rust => "rust",
        }
    }
}

#[derive(Debug, Clone, Copy, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
#[allow(clippy::enum_variant_names)]
pub enum RuneEventType {
    MessageCreate,
    MessageDelete,
    MessageReactionAdd,
    MessageReactionRemove,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct InvocationEnvelope {
    pub execution_id: String,
    pub invocation_id: String,
    pub rune_id: String,
    pub rune_name: String,
    pub guild_id: u64,
    pub language: RuneLanguage,
    pub event_type: RuneEventType,
    pub source: String,
    pub payload: Value,
    pub enqueued_at: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct HostAction {
    pub method: String,
    pub arguments: Value,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GuestResult {
    #[serde(default)]
    pub actions: Vec<HostAction>,
    pub error: Option<String>,
    #[serde(default)]
    pub duration_micros: i64,
}

impl InvocationEnvelope {
    pub fn complete(&self, guest: GuestResult) -> OwnedResultEnvelope {
        OwnedResultEnvelope {
            execution_id: self.execution_id.clone(),
            invocation_id: self.invocation_id.clone(),
            rune_id: self.rune_id.clone(),
            rune_name: self.rune_name.clone(),
            guild_id: self.guild_id,
            language: self.language,
            event_type: self.event_type,
            payload: self.payload.clone(),
            actions: guest.actions,
            error: guest.error,
            duration_micros: guest.duration_micros,
        }
    }

    pub fn fail(&self, error: String) -> OwnedResultEnvelope {
        OwnedResultEnvelope {
            execution_id: self.execution_id.clone(),
            invocation_id: self.invocation_id.clone(),
            rune_id: self.rune_id.clone(),
            rune_name: self.rune_name.clone(),
            guild_id: self.guild_id,
            language: self.language,
            event_type: self.event_type,
            payload: self.payload.clone(),
            actions: Vec::new(),
            error: Some(error),
            duration_micros: 0,
        }
    }
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct OwnedResultEnvelope {
    pub execution_id: String,
    pub invocation_id: String,
    pub rune_id: String,
    pub rune_name: String,
    pub guild_id: u64,
    pub language: RuneLanguage,
    pub event_type: RuneEventType,
    pub payload: Value,
    pub actions: Vec<HostAction>,
    pub error: Option<String>,
    pub duration_micros: i64,
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    fn invocation() -> InvocationEnvelope {
        InvocationEnvelope {
            execution_id: "execution-1".into(),
            invocation_id: "invocation-1".into(),
            rune_id: "rune-1".into(),
            rune_name: "hello".into(),
            guild_id: 18_446_744_073_709_551_610,
            language: RuneLanguage::Javascript,
            event_type: RuneEventType::MessageCreate,
            source: "message.reply('hello')".into(),
            payload: json!({
                "id": "18446744073709551612",
                "channelId": "18446744073709551611",
                "author": {
                    "id": "18446744073709551613",
                    "username": "Ada"
                }
            }),
            enqueued_at: "2026-08-30T00:00:00Z".into(),
        }
    }

    #[test]
    fn csharp_wire_shape_deserializes_and_serializes_with_the_same_contract() {
        let wire = json!({
            "executionId": "execution-1",
            "invocationId": "invocation-1",
            "runeId": "rune-1",
            "runeName": "hello",
            "guildId": 18446744073709551610_u64,
            "language": "javaScript",
            "eventType": "messageCreate",
            "source": "message.reply('hello')",
            "payload": {
                "id": "18446744073709551612",
                "channelId": "18446744073709551611",
                "author": {
                    "id": "18446744073709551613",
                    "username": "Ada"
                }
            },
            "enqueuedAt": "2026-08-30T00:00:00Z"
        });

        let envelope: InvocationEnvelope = serde_json::from_value(wire).unwrap();

        assert_eq!(envelope.language, RuneLanguage::Javascript);
        assert!(matches!(envelope.event_type, RuneEventType::MessageCreate));
        assert_eq!(
            envelope.payload["author"]["id"],
            "18446744073709551613"
        );

        let result = envelope.complete(GuestResult {
            actions: vec![HostAction {
                method: "message.reply".into(),
                arguments: json!({ "content": "hello" }),
            }],
            error: None,
            duration_micros: 123,
        });
        let serialized = serde_json::to_value(result).unwrap();

        assert_eq!(serialized["language"], "javaScript");
        assert_eq!(serialized["eventType"], "messageCreate");
        assert_eq!(serialized["guildId"], 18_446_744_073_709_551_610_u64);
        assert_eq!(serialized["actions"][0]["method"], "message.reply");
        assert_eq!(serialized["payload"]["id"], "18446744073709551612");
    }

    #[test]
    fn failed_invocation_preserves_identity_and_payload_but_never_actions() {
        let envelope = invocation();
        let failed = envelope.fail("timed out".into());
        let serialized = serde_json::to_value(failed).unwrap();

        assert_eq!(serialized["executionId"], "execution-1");
        assert_eq!(serialized["invocationId"], "invocation-1");
        assert_eq!(serialized["runeId"], "rune-1");
        assert_eq!(serialized["payload"], envelope.payload);
        assert_eq!(serialized["actions"], json!([]));
        assert_eq!(serialized["error"], "timed out");
        assert_eq!(serialized["durationMicros"], 0);
    }
}
