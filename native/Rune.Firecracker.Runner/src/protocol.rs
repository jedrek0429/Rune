use serde::{Deserialize, Serialize};
use serde_json::Value;

pub const MAX_ARTIFACT_BYTES: u64 = 16 * 1024 * 1024;

#[derive(Debug, Clone, Copy, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum RuneEventType {
    MessageCreate,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BuiltRuneArtifact {
    pub id: String,
    pub digest: String,
    pub entrypoint: String,
    pub size_bytes: u64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct InvocationEnvelope {
    pub execution_id: String,
    pub invocation_id: String,
    pub rune_id: String,
    pub rune_name: String,
    pub guild_id: u64,
    pub event_type: RuneEventType,
    pub artifact: BuiltRuneArtifact,
    pub payload: Value,
    pub enqueued_at: String,
}

impl InvocationEnvelope {
    pub fn validate(&self) -> Result<(), &'static str> {
        if self.artifact.size_bytes == 0 || self.artifact.size_bytes > MAX_ARTIFACT_BYTES {
            return Err("Rune artifact exceeds the invocation artifact limit");
        }
        if self.artifact.id != self.artifact.digest
            || self.artifact.entrypoint != "rune"
            || !canonical_sha256(&self.artifact.id)
        {
            return Err("Rune artifact descriptor is invalid");
        }
        Ok(())
    }

    pub fn complete(&self, guest: GuestResult) -> ResultEnvelope {
        ResultEnvelope {
            execution_id: self.execution_id.clone(),
            invocation_id: self.invocation_id.clone(),
            rune_id: self.rune_id.clone(),
            actions: guest.actions,
            error: guest.error,
        }
    }

    pub fn fail(&self, error: String) -> ResultEnvelope {
        ResultEnvelope {
            execution_id: self.execution_id.clone(),
            invocation_id: self.invocation_id.clone(),
            rune_id: self.rune_id.clone(),
            actions: Vec::new(),
            error: Some(error),
        }
    }
}

fn canonical_sha256(value: &str) -> bool {
    let Some(hex) = value.strip_prefix("sha256:") else { return false; };
    hex.len() == 64 && hex.bytes().all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
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
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ResultEnvelope {
    pub execution_id: String,
    pub invocation_id: String,
    pub rune_id: String,
    pub actions: Vec<HostAction>,
    pub error: Option<String>,
}

#[cfg(test)]
mod tests {
    use super::*;

    fn envelope(id: &str) -> InvocationEnvelope {
        InvocationEnvelope {
            execution_id: "e".into(), invocation_id: "i".into(), rune_id: "r".into(), rune_name: "n".into(), guild_id: 1,
            event_type: RuneEventType::MessageCreate,
            artifact: BuiltRuneArtifact { id: id.into(), digest: id.into(), entrypoint: "rune".into(), size_bytes: 1 },
            payload: Value::Null, enqueued_at: "now".into(),
        }
    }

    #[test]
    fn only_canonical_content_addressed_artifacts_are_accepted() {
        assert!(envelope("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa").validate().is_ok());
        assert!(envelope("../rune").validate().is_err());
    }
}
