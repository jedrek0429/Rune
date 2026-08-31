mod config;
mod firecracker;
mod pool;
mod protocol;
mod queue;

use anyhow::Result;
use config::Config;
use firecracker::load_artifact;
use pool::VmPool;
use queue::RedisQueue;
use std::sync::Arc;
use tokio::task::JoinSet;
use tracing::{error, info};

const INVOCATION_STREAM: &str = "rune:invocations";
const RESULT_STREAM: &str = "rune:results";
const CONSUMER_GROUP: &str = "rune-runners";

#[tokio::main]
async fn main() -> Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| "rune_firecracker_runner=info".into()),
        )
        .init();
    debug_assert_eq!(INVOCATION_STREAM, "rune:invocations");
    debug_assert_eq!(RESULT_STREAM, "rune:results");
    debug_assert_eq!(CONSUMER_GROUP, "rune-runners");
    let config = Arc::new(Config::from_env()?);
    config.validate_host()?;
    let queue = Arc::new(RedisQueue::connect(config.clone()).await?);
    queue.ensure_group().await?;
    let pool = Arc::new(VmPool::new(config));
    pool.prime().await?;
    let mut tasks = JoinSet::new();
    {
        let p = pool.clone();
        tasks.spawn(async move {
            p.maintain().await;
            Ok::<(), anyhow::Error>(())
        });
    }
    {
        let p = pool.clone();
        let q = queue.clone();
        tasks.spawn(async move {
            autoscale(p, q).await;
            Ok::<(), anyhow::Error>(())
        });
    }
    {
        let p = pool.clone();
        let q = queue.clone();
        tasks.spawn(async move { consume(p, q).await });
    }
    info!("Rune Firecracker runner ready");
    while let Some(result) = tasks.join_next().await {
        result??;
    }
    Ok(())
}

async fn consume(pool: Arc<VmPool>, queue: Arc<RedisQueue>) -> Result<()> {
    loop {
        for job in queue.read().await? {
            let envelope = job.envelope.clone();
            if let Err(message) = envelope.validate() {
                queue.finish(job, &envelope.fail(message.into())).await?;
                continue;
            }
            let artifact =
                match load_artifact(&pool.config().artifact_root(), &envelope.artifact).await {
                    Ok(a) => a,
                    Err(e) => {
                        queue.finish(job, &envelope.fail(e.to_string())).await?;
                        continue;
                    }
                };
            let mut vm = pool.acquire().await?;
            let result = vm.invoke(&envelope, &artifact).await;
            vm.destroy().await;
            pool.complete_invocation();
            let result = match result {
                Ok(r) => envelope.complete(r),
                Err(e) => envelope.fail(e.to_string()),
            };
            queue.finish(job, &result).await?;
        }
    }
}
async fn autoscale(pool: Arc<VmPool>, queue: Arc<RedisQueue>) {
    loop {
        match queue.backlog().await {
            Ok(n) => pool.set_target(pool.target_for_backlog(n)),
            Err(e) => error!(%e,"Redis backlog failed"),
        }
        tokio::time::sleep(pool.config().autoscale_interval).await;
    }
}
