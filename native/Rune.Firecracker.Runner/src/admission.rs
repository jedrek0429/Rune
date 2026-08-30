use std::{collections::HashMap, sync::Arc};

use tokio::sync::{Mutex, OwnedSemaphorePermit, Semaphore};

use crate::protocol::InvocationEnvelope;

pub struct AdmissionController {
    per_rune: usize,
    per_guild: usize,
    runes: Mutex<HashMap<String, Arc<Semaphore>>>,
    guilds: Mutex<HashMap<u64, Arc<Semaphore>>>,
}

pub struct AdmissionPermit {
    _rune: OwnedSemaphorePermit,
    _guild: OwnedSemaphorePermit,
}

impl AdmissionController {
    pub fn new(per_rune: usize, per_guild: usize) -> Self {
        Self {
            per_rune,
            per_guild,
            runes: Mutex::new(HashMap::new()),
            guilds: Mutex::new(HashMap::new()),
        }
    }

    pub async fn acquire(&self, envelope: &InvocationEnvelope) -> AdmissionPermit {
        let rune = {
            let mut runes = self.runes.lock().await;
            runes
                .entry(envelope.rune_id.clone())
                .or_insert_with(|| Arc::new(Semaphore::new(self.per_rune)))
                .clone()
        };
        let guild = {
            let mut guilds = self.guilds.lock().await;
            guilds
                .entry(envelope.guild_id)
                .or_insert_with(|| Arc::new(Semaphore::new(self.per_guild)))
                .clone()
        };
        let guild_permit = guild.acquire_owned().await.expect("guild semaphore closed");
        let rune_permit = rune.acquire_owned().await.expect("rune semaphore closed");
        AdmissionPermit {
            _rune: rune_permit,
            _guild: guild_permit,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::protocol::{BuiltRuneArtifact, RuneEventType, RuneLanguage};
    use serde_json::Value;
    use std::time::Duration;

    fn envelope(rune_id: &str, guild_id: u64) -> InvocationEnvelope {
        InvocationEnvelope {
            execution_id: "execution".into(),
            invocation_id: "invocation".into(),
            rune_id: rune_id.into(),
            rune_name: rune_id.into(),
            guild_id,
            language: RuneLanguage::Rust,
            event_type: RuneEventType::MessageCreate,
            artifact: BuiltRuneArtifact {
                id: "artifact".into(),
                digest: "sha256:test".into(),
                entrypoint: "rune".into(),
                size_bytes: 1,
            },
            payload: Value::Null,
            enqueued_at: "now".into(),
        }
    }

    #[tokio::test]
    async fn second_invocation_of_same_rune_waits_for_first() {
        let controller = AdmissionController::new(1, 4);
        let invocation = envelope("rune-a", 42);
        let first = controller.acquire(&invocation).await;
        assert!(
            tokio::time::timeout(Duration::from_millis(20), controller.acquire(&invocation))
                .await
                .is_err()
        );
        drop(first);
        tokio::time::timeout(Duration::from_millis(100), controller.acquire(&invocation))
            .await
            .expect("permit should become available");
    }

    #[tokio::test]
    async fn guild_limit_applies_across_different_runes() {
        let controller = AdmissionController::new(1, 2);
        let _a = controller.acquire(&envelope("a", 42)).await;
        let _b = controller.acquire(&envelope("b", 42)).await;
        assert!(
            tokio::time::timeout(
                Duration::from_millis(20),
                controller.acquire(&envelope("c", 42))
            )
            .await
            .is_err()
        );
    }
}
