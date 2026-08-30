use std::{env, path::PathBuf, time::Duration};

use anyhow::{Context, Result, bail};

use crate::protocol::{InvocationRuntime, RuneLanguage};

#[derive(Debug)]
pub struct Config {
    pub redis_url: String,
    pub invocation_stream_prefix: String,
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
    pub result_stream_max_len: usize,
    pub read_batch_size: usize,
    pub max_concurrent_per_rune: usize,
    pub max_concurrent_per_guild: usize,
}

impl Config {
    pub fn from_env() -> Result<Self> {
        let hostname = env::var("HOSTNAME").unwrap_or_else(|_| "runner".into());
        let pid = std::process::id();

        let config = Self {
            redis_url: env::var("RUNE_REDIS_URL")
                .unwrap_or_else(|_| "redis://127.0.0.1:6379/".into()),
            invocation_stream_prefix: env::var("RUNE_INVOCATION_STREAM_PREFIX")
                .unwrap_or_else(|_| "rune:invocations".into()),
            result_stream: env::var("RUNE_RESULT_STREAM").unwrap_or_else(|_| "rune:results".into()),
            consumer_group: env::var("RUNE_RUNNER_GROUP").unwrap_or_else(|_| "rune-runners".into()),
            consumer_name: env::var("RUNE_RUNNER_NAME")
                .unwrap_or_else(|_| format!("{hostname}-{pid}")),
            firecracker_binary: env::var_os("RUNE_FIRECRACKER")
                .map(PathBuf::from)
                .unwrap_or_else(|| PathBuf::from("firecracker")),
            state_root: env::var_os("RUNE_FIRECRACKER_ROOT")
                .map(PathBuf::from)
                .unwrap_or_else(|| PathBuf::from("/var/lib/rune/firecracker")),
            min_vms: parse("RUNE_VM_MIN", 1)?,
            max_vms: parse("RUNE_VM_MAX", 8)?,
            backlog_per_vm: parse("RUNE_VM_BACKLOG_PER_VM", 4)?,
            invocation_timeout: Duration::from_millis(parse("RUNE_INVOCATION_TIMEOUT_MS", 3_000)?),
            restore_timeout: Duration::from_millis(parse("RUNE_VM_RESTORE_TIMEOUT_MS", 2_000)?),
            autoscale_interval: Duration::from_millis(parse("RUNE_VM_AUTOSCALE_INTERVAL_MS", 250)?),
            result_stream_max_len: parse("RUNE_RESULT_STREAM_MAX_LEN", 10_000)?,
            read_batch_size: parse("RUNE_REDIS_BATCH", 32)?,
            max_concurrent_per_rune: parse("RUNE_MAX_CONCURRENT_PER_RUNE", 1)?,
            max_concurrent_per_guild: parse("RUNE_MAX_CONCURRENT_PER_GUILD", 4)?,
        };

        if config.min_vms == 0 {
            bail!("RUNE_VM_MIN must be at least 1");
        }
        if config.max_vms < config.min_vms {
            bail!("RUNE_VM_MAX must be greater than or equal to RUNE_VM_MIN");
        }
        if config.backlog_per_vm == 0 {
            bail!("RUNE_VM_BACKLOG_PER_VM must be at least 1");
        }
        if config.max_concurrent_per_rune == 0 || config.max_concurrent_per_guild == 0 {
            bail!("Rune concurrency limits must be at least 1");
        }
        if config.max_concurrent_per_guild < config.max_concurrent_per_rune {
            bail!("guild concurrency limit must be >= per-rune concurrency limit");
        }

        Ok(config)
    }

    #[cfg(target_os = "linux")]
    pub fn validate_host(&self) -> Result<()> {
        use std::os::unix::fs::FileTypeExt;

        let metadata = std::fs::metadata("/dev/kvm")
            .context("/dev/kvm is unavailable; enable KVM before starting the Rune runner")?;

        if !metadata.file_type().is_char_device() {
            bail!("/dev/kvm is not a KVM character device");
        }

        self.validate_snapshots()
    }

    #[cfg(not(target_os = "linux"))]
    pub fn validate_host(&self) -> Result<()> {
        bail!("Firecracker runners require Linux with KVM")
    }

    fn validate_snapshots(&self) -> Result<()> {
        for runtime in InvocationRuntime::ALL {
            let snapshot = self.snapshot_path(runtime);
            let memory = self.memory_path(runtime);

            if !snapshot.is_file() {
                bail!(
                    "missing {} snapshot: {}",
                    runtime.as_str(),
                    snapshot.display()
                );
            }
            if !memory.is_file() {
                bail!(
                    "missing {} memory image: {}",
                    runtime.as_str(),
                    memory.display()
                );
            }
        }

        Ok(())
    }

    pub fn invocation_stream(&self, language: RuneLanguage) -> String {
        format!("{}:{}", self.invocation_stream_prefix, language.as_str())
    }

    pub fn snapshot_path(&self, runtime: InvocationRuntime) -> PathBuf {
        self.state_root
            .join("snapshots")
            .join(runtime.as_str())
            .join("vmstate")
    }

    pub fn memory_path(&self, runtime: InvocationRuntime) -> PathBuf {
        self.state_root
            .join("snapshots")
            .join(runtime.as_str())
            .join("memory")
    }

    pub fn runtime_root(&self) -> PathBuf {
        self.state_root.join("runtime")
    }
}

fn parse<T>(name: &str, default: T) -> Result<T>
where
    T: std::str::FromStr + Copy,
    T::Err: std::error::Error + Send + Sync + 'static,
{
    match env::var(name) {
        Ok(value) => value.parse().with_context(|| format!("invalid {name}")),
        Err(_) => Ok(default),
    }
}
