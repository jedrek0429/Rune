use serde::{Deserialize, Serialize};
use serde_json::Value;

pub const MAX_ARTIFACT_BYTES: u64 = 16 * 1024 * 1024;

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
        if self.artifact.size_bytes > MAX_ARTIFACT_BYTES {
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

    pub fn complete(&self, guest: GuestResult) -> OwnedResultEnvelope {
        OwnedResultEnvelope {
            execution_id: self.execution_id.clone(),
            invocation_id: self.invocation_id.clone(),
            rune_id: self.rune_id.clone(),
            rune_name: self.rune_name.clone(),
            guild_id: self.guild_id,
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
            event_type: self.event_type,
            payload: self.payload.clone(),
            actions: Vec::new(),
            error: Some(error),
            duration_micros: 0,
        }
    }
}

fn canonical_sha256(value: &str) -> bool {
    let Some(hex) = value.strip_prefix("sha256:") else {
        return false;
    };
    hex.len() == 64
        && hex
            .bytes()
            .all(|byte| byte.is_ascii_digit() || (b'a'..=b'f').contains(&byte))
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

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct OwnedResultEnvelope {
    pub execution_id: String,
    pub invocation_id: String,
    pub rune_id: String,
    pub rune_name: String,
    pub guild_id: u64,
    pub event_type: RuneEventType,
    pub payload: Value,
    pub actions: Vec<HostAction>,
    pub error: Option<String>,
    pub duration_micros: i64,
}

#[cfg(test)]
mod tests {
    use super::*;

    fn envelope(id: &str, digest: &str) -> InvocationEnvelope {
        InvocationEnvelope {
            execution_id: "e".into(),
            invocation_id: "i".into(),
            rune_id: "r".into(),
            rune_name: "name".into(),
            guild_id: 1,
            event_type: RuneEventType::MessageCreate,
            artifact: BuiltRuneArtifact {
                id: id.into(),
                digest: digest.into(),
                entrypoint: "rune".into(),
                size_bytes: 1,
            },
            payload: Value::Null,
            enqueued_at: "now".into(),
        }
    }

    #[test]
    fn oversized_artifact_is_rejected() {
        let mut envelope = envelope(
            "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        );
        envelope.artifact.size_bytes = MAX_ARTIFACT_BYTES + 1;
        assert!(envelope.validate().is_err());
    }

    #[test]
    fn artifact_identity_must_be_canonical_sha256() {
        assert!(
            envelope(
                "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
            )
            .validate()
            .is_ok()
        );
        assert!(envelope("../artifact", "sha256:abc").validate().is_err());
        assert!(
            envelope(
                "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
            )
            .validate()
            .is_err()
        );
    }
}
