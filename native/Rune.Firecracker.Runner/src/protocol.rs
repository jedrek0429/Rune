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
