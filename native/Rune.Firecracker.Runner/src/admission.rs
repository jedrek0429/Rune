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
