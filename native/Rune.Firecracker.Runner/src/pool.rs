use std::{
    collections::VecDeque,
    sync::{
        Arc,
        atomic::{AtomicUsize, Ordering},
    },
};

use anyhow::Result;
use tokio::sync::{Mutex, Notify};
use tracing::{error, info};

use crate::{config::Config, firecracker::WarmVm};

pub struct VmPool {
    config: Arc<Config>,
    idle: Mutex<VecDeque<WarmVm>>,
    in_flight: AtomicUsize,
    target: AtomicUsize,
    changed: Notify,
}

impl VmPool {
    pub fn new(config: Arc<Config>) -> Self {
        Self {
            target: AtomicUsize::new(config.min_vms),
            config,
            idle: Mutex::new(VecDeque::new()),
            in_flight: AtomicUsize::new(0),
            changed: Notify::new(),
        }
    }

    pub fn config(&self) -> &Config {
        &self.config
    }

    pub async fn prime(&self) -> Result<()> {
        for _ in 0..self.config.min_vms {
            self.idle
                .lock()
                .await
                .push_back(WarmVm::restore(self.config.clone()).await?);
        }
        info!(count = self.config.min_vms, "warm Rune pool primed");
        Ok(())
    }

    pub async fn acquire(&self) -> Result<WarmVm> {
        loop {
            if let Some(vm) = self.idle.lock().await.pop_front() {
                self.in_flight.fetch_add(1, Ordering::AcqRel);
                self.changed.notify_one();
                return Ok(vm);
            }
            self.changed.notify_one();
            self.changed.notified().await;
        }
    }

    pub fn complete_invocation(&self) {
        self.in_flight.fetch_sub(1, Ordering::AcqRel);
        self.changed.notify_one();
    }

    pub fn set_target(&self, target: usize) {
        let target = target.clamp(self.config.min_vms, self.config.max_vms);
        let previous = self.target.swap(target, Ordering::AcqRel);
        if previous != target {
            info!(previous, target, "warm Rune pool target changed");
            self.changed.notify_one();
        }
    }

    pub fn target_for_backlog(&self, backlog: usize) -> usize {
        if backlog == 0 {
            return self.config.min_vms;
        }
        backlog
            .div_ceil(self.config.backlog_per_vm)
            .clamp(self.config.min_vms, self.config.max_vms)
    }

    pub async fn maintain(&self) {
        loop {
            let target = self.target.load(Ordering::Acquire);
            let in_flight = self.in_flight.load(Ordering::Acquire);
            let idle_count = self.idle.lock().await.len();
            let total = idle_count + in_flight;

            if total < target {
                for _ in 0..(target - total) {
                    match WarmVm::restore(self.config.clone()).await {
                        Ok(vm) => {
                            self.idle.lock().await.push_back(vm);
                            self.changed.notify_waiters();
                        }
                        Err(error) => {
                            error!(%error, "failed to restore warm Rune VM");
                            tokio::time::sleep(std::time::Duration::from_millis(100)).await;
                            break;
                        }
                    }
                }
                continue;
            }

            if total > target && idle_count > 0 {
                for _ in 0..(total - target).min(idle_count) {
                    if let Some(vm) = self.idle.lock().await.pop_back() {
                        vm.destroy().await;
                    }
                }
                continue;
            }

            self.changed.notified().await;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::{path::PathBuf, time::Duration};

    fn config(min_vms: usize, max_vms: usize, backlog_per_vm: usize) -> Arc<Config> {
        Arc::new(Config {
            redis_url: "redis://127.0.0.1:6379/".into(),
            invocation_stream: "rune:invocations".into(),
            result_stream: "rune:results".into(),
            consumer_group: "rune-runners".into(),
            consumer_name: "test".into(),
            firecracker_binary: PathBuf::from("firecracker"),
            state_root: PathBuf::from("/tmp/rune-test"),
            min_vms,
            max_vms,
            backlog_per_vm,
            invocation_timeout: Duration::from_secs(3),
            restore_timeout: Duration::from_secs(2),
            autoscale_interval: Duration::from_millis(250),
            result_stream_max_len: 10_000,
            read_batch_size: 32,
            max_concurrent_per_rune: 1,
            max_concurrent_per_guild: 4,
            max_invocations_per_rune_per_second: 10,
            max_invocations_per_guild_per_second: 50,
        })
    }

    #[test]
    fn backlog_target_uses_ceiling_and_respects_pool_bounds() {
        let pool = VmPool::new(config(2, 5, 4));
        assert_eq!(pool.target_for_backlog(0), 2);
        assert_eq!(pool.target_for_backlog(1), 2);
        assert_eq!(pool.target_for_backlog(8), 2);
        assert_eq!(pool.target_for_backlog(9), 3);
        assert_eq!(pool.target_for_backlog(16), 4);
        assert_eq!(pool.target_for_backlog(17), 5);
        assert_eq!(pool.target_for_backlog(1_000), 5);
    }
}
