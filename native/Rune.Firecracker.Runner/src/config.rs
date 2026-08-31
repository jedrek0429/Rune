use std::{env, path::PathBuf, time::Duration};
use anyhow::{Context, Result, bail};

#[derive(Debug)]
pub struct Config {
    pub redis_url: String,
    pub invocation_stream: String,
    pub result_stream: String,
    pub consumer_group: String,
    pub consumer_name: String,
    pub firecracker_binary: PathBuf,
    pub state_root: PathBuf,
    pub min_vms: usize,
    pub max_vms: usize,
    pub backlog_per_vm: usize,
    pub invocation_timeout: Duration,
    pub restore_timeout: Duration,
    pub autoscale_interval: Duration,
    pub read_batch_size: usize,
}

impl Config {
    pub fn from_env() -> Result<Self> {
        let hostname = env::var("HOSTNAME").unwrap_or_else(|_| "runner".into());
        let config = Self {
            redis_url: env::var("RUNE_REDIS_URL").unwrap_or_else(|_| "redis://127.0.0.1:6379/".into()),
            invocation_stream: "rune:invocations".into(),
            result_stream: "rune:results".into(),
            consumer_group: "rune-runners".into(),
            consumer_name: env::var("RUNE_RUNNER_NAME").unwrap_or_else(|_| format!("{hostname}-{}", std::process::id())),
            firecracker_binary: env::var_os("RUNE_FIRECRACKER").map(PathBuf::from).unwrap_or_else(|| "firecracker".into()),
            state_root: env::var_os("RUNE_FIRECRACKER_ROOT").map(PathBuf::from).unwrap_or_else(|| "/var/lib/rune/firecracker".into()),
            min_vms: parse("RUNE_VM_MIN", 1)?, max_vms: parse("RUNE_VM_MAX", 8)?, backlog_per_vm: parse("RUNE_VM_BACKLOG_PER_VM", 4)?,
            invocation_timeout: Duration::from_millis(parse("RUNE_INVOCATION_TIMEOUT_MS", 3_000)?),
            restore_timeout: Duration::from_millis(parse("RUNE_VM_RESTORE_TIMEOUT_MS", 2_000)?),
            autoscale_interval: Duration::from_millis(parse("RUNE_VM_AUTOSCALE_INTERVAL_MS", 250)?),
            read_batch_size: parse("RUNE_REDIS_BATCH", 32)?,
        };
        if config.min_vms == 0 || config.max_vms < config.min_vms || config.backlog_per_vm == 0 { bail!("invalid VM pool bounds"); }
        Ok(config)
    }

    pub fn validate_host(&self) -> Result<()> {
        std::fs::metadata("/dev/kvm").context("/dev/kvm is unavailable")?;
        for path in [self.snapshot_path(), self.memory_path()] {
            if !path.is_file() { bail!("missing Rune snapshot file: {}", path.display()); }
        }
        Ok(())
    }
    pub fn snapshot_path(&self) -> PathBuf { self.state_root.join("snapshot/vmstate") }
    pub fn memory_path(&self) -> PathBuf { self.state_root.join("snapshot/memory") }
    pub fn runtime_root(&self) -> PathBuf { self.state_root.join("runtime") }
    pub fn artifact_root(&self) -> PathBuf { self.state_root.join("artifacts") }
}

fn parse<T>(name: &str, default: T) -> Result<T>
where T: std::str::FromStr + Copy, T::Err: std::error::Error + Send + Sync + 'static {
    match env::var(name) { Ok(value) => value.parse().with_context(|| format!("invalid {name}")), Err(_) => Ok(default) }
}
