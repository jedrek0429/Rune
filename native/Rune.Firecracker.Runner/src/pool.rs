use std::{collections::VecDeque, sync::{Arc, atomic::{AtomicUsize, Ordering}}};
use anyhow::Result;
use tokio::sync::{Mutex, Notify};
use crate::{config::Config, firecracker::WarmVm};

pub struct VmPool { config: Arc<Config>, idle: Mutex<VecDeque<WarmVm>>, in_flight: AtomicUsize, target: AtomicUsize, changed: Notify }
impl VmPool {
    pub fn new(config: Arc<Config>) -> Self { Self { target: AtomicUsize::new(config.min_vms), config, idle: Mutex::new(VecDeque::new()), in_flight: AtomicUsize::new(0), changed: Notify::new() } }
    pub fn config(&self) -> &Config { &self.config }
    pub async fn prime(&self) -> Result<()> { for _ in 0..self.config.min_vms { self.idle.lock().await.push_back(WarmVm::restore(self.config.clone()).await?); } Ok(()) }
    pub async fn acquire(&self) -> Result<WarmVm> { loop { if let Some(vm)=self.idle.lock().await.pop_front() { self.in_flight.fetch_add(1, Ordering::AcqRel); self.changed.notify_one(); return Ok(vm); } self.changed.notify_one(); self.changed.notified().await; } }
    pub fn complete_invocation(&self) { self.in_flight.fetch_sub(1, Ordering::AcqRel); self.changed.notify_one(); }
    pub fn set_target(&self, target: usize) { self.target.store(target.clamp(self.config.min_vms, self.config.max_vms), Ordering::Release); self.changed.notify_one(); }
    pub fn target_for_backlog(&self, backlog: usize) -> usize { if backlog==0 { return self.config.min_vms; } backlog.div_ceil(self.config.backlog_per_vm).clamp(self.config.min_vms, self.config.max_vms) }
    pub async fn maintain(&self) { loop {
        let target=self.target.load(Ordering::Acquire); let in_flight=self.in_flight.load(Ordering::Acquire); let idle=self.idle.lock().await.len(); let total=idle+in_flight;
        if total < target { match WarmVm::restore(self.config.clone()).await { Ok(vm)=>{ self.idle.lock().await.push_back(vm); self.changed.notify_waiters(); }, Err(_)=>tokio::time::sleep(Duration::from_millis(100)).await } continue; }
        if total > target && idle > 0 { if let Some(vm)=self.idle.lock().await.pop_back() { vm.destroy().await; } continue; }
        self.changed.notified().await;
    }}
}
use std::time::Duration;

#[cfg(test)] mod tests { use super::*; use std::{path::PathBuf, time::Duration};
fn config()->Arc<Config>{Arc::new(Config{redis_url:"redis://127.0.0.1/".into(),invocation_stream:"rune:invocations".into(),result_stream:"rune:results".into(),consumer_group:"rune-runners".into(),consumer_name:"test".into(),firecracker_binary:PathBuf::from("firecracker"),state_root:PathBuf::from("/tmp/rune"),min_vms:1,max_vms:4,backlog_per_vm:2,invocation_timeout:Duration::from_secs(3),restore_timeout:Duration::from_secs(2),autoscale_interval:Duration::from_millis(250),read_batch_size:32})}
#[test] fn backlog_scales_pool_with_ceiling(){let p=VmPool::new(config()); assert_eq!(p.target_for_backlog(0),1);assert_eq!(p.target_for_backlog(3),2);assert_eq!(p.target_for_backlog(99),4);}}
