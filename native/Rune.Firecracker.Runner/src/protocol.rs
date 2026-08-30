use serde::{Deserialize, Serialize};
use serde_json::Value;

pub const MAX_ARTIFACT_BYTES: u64 = 16 * 1024 * 1024;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub enum RuneLanguage {
    #[serde(rename = "javaScript")]
    Javascript,
    #[serde(rename = "typeScript")]
    Typescript,
    #[serde(rename = "python")]
    Python,
    #[serde(rename = "ruby")]
    Ruby,
    #[serde(rename = "rust")]
    Rust,
    #[serde(rename = "c")]
    C,
    #[serde(rename = "cpp")]
    Cpp,
    #[serde(rename = "cSharp")]
    Csharp,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum InvocationRuntime {
    Native,
    Python,
    Ruby,
}

impl InvocationRuntime {
    pub const ALL: [Self; 3] = [Self::Native, Self::Python, Self::Ruby];

    pub const fn as_str(self) -> &'static str {
        match self {
            Self::Native => "native",
            Self::Python => "python",
            Self::Ruby => "ruby",
        }
    }

    pub const fn memory_mib(self) -> usize {
        match self {
            Self::Native => 192,
            Self::Python | Self::Ruby => 256,
        }
    }
}

impl RuneLanguage {
    pub const ALL: [Self; 8] = [
        Self::Javascript,
        Self::Typescript,
        Self::Python,
        Self::Ruby,
        Self::Rust,
        Self::C,
        Self::Cpp,
        Self::Csharp,
    ];

    pub const fn as_str(self) -> &'static str {
        match self {
            Self::Javascript => "javascript",
            Self::Typescript => "typescript",
            Self::Python => "python",
            Self::Ruby => "ruby",
            Self::Rust => "rust",
            Self::C => "c",
            Self::Cpp => "cpp",
            Self::Csharp => "csharp",
        }
    }

    pub const fn invocation_runtime(self) -> InvocationRuntime {
        match self {
            Self::Javascript
            | Self::Typescript
            | Self::Rust
            | Self::C
            | Self::Cpp
            | Self::Csharp => InvocationRuntime::Native,
            Self::Python => InvocationRuntime::Python,
            Self::Ruby => InvocationRuntime::Ruby,
        }
    }

    pub const fn build_pool(self) -> &'static str {
        match self {
            Self::Javascript | Self::Typescript => "scriptc",
            Self::Rust => "rust",
            Self::C | Self::Cpp => "clang",
            Self::Csharp => "dotnet-aot",
            Self::Python => "python",
            Self::Ruby => "ruby",
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
    pub language: RuneLanguage,
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
        if self.artifact.id.is_empty()
            || self.artifact.digest.is_empty()
            || self.artifact.entrypoint.is_empty()
        {
            return Err("Rune artifact descriptor is incomplete");
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

    #[test]
    fn native_languages_share_one_invocation_runtime() {
        for language in [
            RuneLanguage::Javascript,
            RuneLanguage::Typescript,
            RuneLanguage::Rust,
            RuneLanguage::C,
            RuneLanguage::Cpp,
            RuneLanguage::Csharp,
        ] {
            assert_eq!(language.invocation_runtime(), InvocationRuntime::Native);
        }
    }

    #[test]
    fn csharp_builds_with_native_aot() {
        assert_eq!(RuneLanguage::Csharp.build_pool(), "dotnet-aot");
    }

    #[test]
    fn oversized_artifact_is_rejected() {
        let envelope = InvocationEnvelope {
            execution_id: "e".into(),
            invocation_id: "i".into(),
            rune_id: "r".into(),
            rune_name: "name".into(),
            guild_id: 1,
            language: RuneLanguage::Rust,
            event_type: RuneEventType::MessageCreate,
            artifact: BuiltRuneArtifact {
                id: "a".into(),
                digest: "sha256:x".into(),
                entrypoint: "rune".into(),
                size_bytes: MAX_ARTIFACT_BYTES + 1,
            },
            payload: Value::Null,
            enqueued_at: "now".into(),
        };

        assert!(envelope.validate().is_err());
    }
}
