use std::{
    collections::{HashMap, VecDeque},
    sync::Arc,
    time::{Duration, Instant},
};

use tokio::sync::{Mutex, OwnedSemaphorePermit, Semaphore};

use crate::protocol::InvocationEnvelope;

const RATE_WINDOW: Duration = Duration::from_secs(1);

pub struct AdmissionController {
    per_rune: usize,
    per_guild: usize,
    per_rune_rate: usize,
    per_guild_rate: usize,
    runes: Mutex<HashMap<String, Arc<Semaphore>>>,
    guilds: Mutex<HashMap<u64, Arc<Semaphore>>>,
    rune_windows: Mutex<HashMap<String, VecDeque<Instant>>>,
    guild_windows: Mutex<HashMap<u64, VecDeque<Instant>>>,
}

#[derive(Debug)]
pub struct AdmissionPermit {
    _rune: OwnedSemaphorePermit,
    _guild: OwnedSemaphorePermit,
}

impl AdmissionController {
    pub fn new(
        per_rune: usize,
        per_guild: usize,
        per_rune_rate: usize,
        per_guild_rate: usize,
    ) -> Self {
        Self::with_rates(per_rune, per_guild, per_rune_rate, per_guild_rate)
    }

    pub fn with_rates(
        per_rune: usize,
        per_guild: usize,
        per_rune_rate: usize,
        per_guild_rate: usize,
    ) -> Self {
        Self {
            per_rune,
            per_guild,
            per_rune_rate,
            per_guild_rate,
            runes: Mutex::new(HashMap::new()),
            guilds: Mutex::new(HashMap::new()),
            rune_windows: Mutex::new(HashMap::new()),
            guild_windows: Mutex::new(HashMap::new()),
        }
    }

    pub async fn acquire(
        &self,
        envelope: &InvocationEnvelope,
    ) -> Result<AdmissionPermit, &'static str> {
        let now = Instant::now();
        let mut rune_windows = self.rune_windows.lock().await;
        if !consume_rate_slot(
            &mut *rune_windows,
            envelope.rune_id.clone(),
            self.per_rune_rate,
            now,
        ) {
            return Err("Rune invocation rate limit exceeded");
        }
        drop(rune_windows);

        let mut guild_windows = self.guild_windows.lock().await;
        if !consume_rate_slot(
            &mut *guild_windows,
            envelope.guild_id,
            self.per_guild_rate,
            now,
        ) {
            return Err("Guild invocation rate limit exceeded");
        }
        drop(guild_windows);

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
        Ok(AdmissionPermit {
            _rune: rune_permit,
            _guild: guild_permit,
        })
    }
}

fn consume_rate_slot<K>(
    windows: &mut HashMap<K, VecDeque<Instant>>,
    key: K,
    limit: usize,
    now: Instant,
) -> bool
where
    K: std::hash::Hash + Eq,
{
    let window = windows.entry(key).or_default();
    while window
        .front()
        .is_some_and(|timestamp| now.duration_since(*timestamp) >= RATE_WINDOW)
    {
        window.pop_front();
    }
    if window.len() >= limit {
        return false;
    }
    window.push_back(now);
    true
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
        let controller = AdmissionController::with_rates(1, 4, 100, 100);
        let invocation = envelope("rune-a", 42);
        let first = controller.acquire(&invocation).await.unwrap();
        assert!(
            tokio::time::timeout(Duration::from_millis(20), controller.acquire(&invocation))
                .await
                .is_err()
        );
        drop(first);
        tokio::time::timeout(Duration::from_millis(100), controller.acquire(&invocation))
            .await
            .expect("permit should become available")
            .unwrap();
    }

    #[tokio::test]
    async fn guild_concurrency_limit_applies_across_different_runes() {
        let controller = AdmissionController::with_rates(1, 2, 100, 100);
        let _a = controller.acquire(&envelope("a", 42)).await.unwrap();
        let _b = controller.acquire(&envelope("b", 42)).await.unwrap();
        assert!(
            tokio::time::timeout(
                Duration::from_millis(20),
                controller.acquire(&envelope("c", 42))
            )
            .await
            .is_err()
        );
    }

    #[tokio::test]
    async fn per_rune_rate_limit_rejects_excess_burst() {
        let controller = AdmissionController::with_rates(1, 4, 2, 100);
        let invocation = envelope("rune-a", 42);

        drop(controller.acquire(&invocation).await.unwrap());
        drop(controller.acquire(&invocation).await.unwrap());
        let error = controller.acquire(&invocation).await.unwrap_err();

        assert_eq!(error, "Rune invocation rate limit exceeded");
    }

    #[tokio::test]
    async fn per_guild_rate_limit_is_shared_across_runes() {
        let controller = AdmissionController::with_rates(1, 4, 100, 2);

        drop(controller.acquire(&envelope("a", 42)).await.unwrap());
        drop(controller.acquire(&envelope("b", 42)).await.unwrap());
        let error = controller.acquire(&envelope("c", 42)).await.unwrap_err();

        assert_eq!(error, "Guild invocation rate limit exceeded");
    }
}
