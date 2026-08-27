mod config;
mod firecracker;
mod pool;
mod protocol;
mod redis_queue;

use std::sync::Arc;

use anyhow::Result;
use pool::VmPool;
use protocol::RuneLanguage;
use redis_queue::RedisQueue;
use tokio::task::JoinSet;
use tracing::{error, info};

use crate::config::Config;

#[tokio::main]
async fn main() -> Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| "rune_firecracker_runner=info".into()),
        )
        .init();

    let config = Arc::new(Config::from_env()?);
    config.validate_host()?;

    let queue = Arc::new(RedisQueue::connect(config.clone()).await?);
    let mut tasks = JoinSet::new();

    for language in RuneLanguage::ALL {
        let pool = Arc::new(VmPool::new(language, config.clone()));
        pool.prime().await?;

        {
            let pool = pool.clone();
            tasks.spawn(async move {
                pool.maintain().await;
                Ok::<(), anyhow::Error>(())
            });
        }

        {
            let pool = pool.clone();
            let queue = queue.clone();
            tasks.spawn(async move {
                autoscale(language, pool, queue).await;
                Ok::<(), anyhow::Error>(())
            });
        }

        {
            let pool = pool.clone();
            let queue = queue.clone();
            tasks.spawn(async move { consume(language, pool, queue).await });
        }
    }

    info!("Rune Firecracker runner is ready");

    while let Some(result) = tasks.join_next().await {
        match result {
            Ok(Ok(())) => {}
            Ok(Err(error)) => {
                error!(%error, "runner task failed");
                return Err(error);
            }
            Err(error) => {
                return Err(error.into());
            }
        }
    }

    Ok(())
}

async fn consume(
    language: RuneLanguage,
    pool: Arc<VmPool>,
    queue: Arc<RedisQueue>,
) -> Result<()> {
    queue.ensure_group(language).await?;

    loop {
        let jobs = queue.read(language).await?;
        if jobs.is_empty() {
            continue;
        }

        let mut invocations = JoinSet::new();

        for job in jobs {
            let pool = pool.clone();
            let queue = queue.clone();

            invocations.spawn(async move {
                let envelope = job.envelope.clone();
                let mut vm = pool.acquire().await?;

                let result = vm.invoke(&envelope).await;
                vm.destroy().await;
                pool.complete_invocation();

                let result = match result {
                    Ok(guest_result) => envelope.complete(guest_result),
                    Err(error) => envelope.fail(error.to_string()),
                };

                queue.finish(job, &result).await
            });
        }

        while let Some(result) = invocations.join_next().await {
            match result {
                Ok(Ok(())) => {}
                Ok(Err(error)) => error!(%error, "failed to finish Rune invocation"),
                Err(error) => error!(%error, "Rune invocation task panicked"),
            }
        }
    }
}

async fn autoscale(
    language: RuneLanguage,
    pool: Arc<VmPool>,
    queue: Arc<RedisQueue>,
) {
    let interval = pool.config().autoscale_interval;

    loop {
        match queue.backlog(language).await {
            Ok(backlog) => {
                let target = pool.target_for_backlog(backlog);
                pool.set_target(target);
            }
            Err(error) => {
                error!(?language, %error, "failed to measure Redis backlog");
            }
        }

        tokio::time::sleep(interval).await;
    }
}
