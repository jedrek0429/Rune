mod admission;
mod config;
mod firecracker;
mod pool;
mod protocol;
mod redis_queue;

use std::sync::Arc;

use admission::AdmissionController;
use anyhow::Result;
use pool::VmPool;
use protocol::{InvocationRuntime, RuneLanguage};
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
    let admission = Arc::new(AdmissionController::new(
        config.max_concurrent_per_rune,
        config.max_concurrent_per_guild,
    ));
    let mut tasks = JoinSet::new();

    for runtime in InvocationRuntime::ALL {
        let pool = Arc::new(VmPool::new(runtime, config.clone()));
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
                autoscale(runtime, pool, queue).await;
                Ok::<(), anyhow::Error>(())
            });
        }

        for language in languages_for_runtime(runtime) {
            let pool = pool.clone();
            let queue = queue.clone();
            let admission = admission.clone();
            tasks.spawn(async move { consume(language, pool, queue, admission).await });
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
            Err(error) => return Err(error.into()),
        }
    }

    Ok(())
}

fn languages_for_runtime(runtime: InvocationRuntime) -> impl Iterator<Item = RuneLanguage> {
    RuneLanguage::ALL
        .into_iter()
        .filter(move |language| language.invocation_runtime() == runtime)
}

async fn consume(
    language: RuneLanguage,
    pool: Arc<VmPool>,
    queue: Arc<RedisQueue>,
    admission: Arc<AdmissionController>,
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
            let admission = admission.clone();

            invocations.spawn(async move {
                let envelope = job.envelope.clone();
                if let Err(message) = envelope.validate() {
                    return queue.finish(job, &envelope.fail(message.into())).await;
                }
                if envelope.language.invocation_runtime() != pool.runtime() {
                    return queue
                        .finish(
                            job,
                            &envelope
                                .fail("Rune was routed to the wrong invocation runtime".into()),
                        )
                        .await;
                }

                let _admission = admission.acquire(&envelope).await;
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

async fn autoscale(runtime: InvocationRuntime, pool: Arc<VmPool>, queue: Arc<RedisQueue>) {
    let interval = pool.config().autoscale_interval;

    loop {
        match queue.backlog_for_runtime(runtime).await {
            Ok(backlog) => {
                let target = pool.target_for_backlog(backlog);
                pool.set_target(target);
            }
            Err(error) => {
                error!(?runtime, %error, "failed to measure Redis backlog");
            }
        }

        tokio::time::sleep(interval).await;
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn native_runtime_owns_all_native_compiled_languages() {
        let languages: Vec<_> = languages_for_runtime(InvocationRuntime::Native).collect();
        assert_eq!(
            languages,
            vec![
                RuneLanguage::Javascript,
                RuneLanguage::Typescript,
                RuneLanguage::Rust,
                RuneLanguage::C,
                RuneLanguage::Cpp,
                RuneLanguage::Csharp,
            ]
        );
    }
}
