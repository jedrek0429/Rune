use std::sync::Arc;

use anyhow::{Context, Result};
use redis::{AsyncCommands, Client, FromRedisValue, RedisError, streams::StreamReadReply};
use tracing::debug;

use crate::{
    config::Config,
    protocol::{InvocationEnvelope, InvocationRuntime, OwnedResultEnvelope, RuneLanguage},
};

#[derive(Clone)]
pub struct RedisQueue {
    client: Client,
    config: Arc<Config>,
}

#[derive(Debug, Clone)]
pub struct QueueJob {
    pub stream: String,
    pub id: String,
    pub envelope: InvocationEnvelope,
}

impl RedisQueue {
    pub async fn connect(config: Arc<Config>) -> Result<Self> {
        let client = Client::open(config.redis_url.clone())?;
        let mut connection = client.get_multiplexed_async_connection().await?;
        let _: String = redis::cmd("PING").query_async(&mut connection).await?;
        Ok(Self { client, config })
    }

    pub async fn ensure_group(&self, language: RuneLanguage) -> Result<()> {
        let stream = self.config.invocation_stream(language);
        let mut connection = self.client.get_multiplexed_async_connection().await?;

        let result: Result<String, RedisError> = redis::cmd("XGROUP")
            .arg("CREATE")
            .arg(&stream)
            .arg(&self.config.consumer_group)
            .arg("0-0")
            .arg("MKSTREAM")
            .query_async(&mut connection)
            .await;

        match result {
            Ok(_) => Ok(()),
            Err(error) if error.to_string().contains("BUSYGROUP") => Ok(()),
            Err(error) => Err(error.into()),
        }
    }

    pub async fn read(&self, language: RuneLanguage) -> Result<Vec<QueueJob>> {
        let stream = self.config.invocation_stream(language);
        let mut connection = self.client.get_multiplexed_async_connection().await?;

        let reply: StreamReadReply = redis::cmd("XREADGROUP")
            .arg("GROUP")
            .arg(&self.config.consumer_group)
            .arg(&self.config.consumer_name)
            .arg("COUNT")
            .arg(self.config.read_batch_size)
            .arg("BLOCK")
            .arg(1_000)
            .arg("STREAMS")
            .arg(&stream)
            .arg(">")
            .query_async(&mut connection)
            .await?;

        let mut jobs = Vec::new();

        for key in reply.keys {
            for id in key.ids {
                let Some(value) = id.map.get("json") else {
                    continue;
                };

                let json = String::from_redis_value(value)
                    .context("Redis invocation did not contain UTF-8 JSON")?;
                let envelope: InvocationEnvelope = serde_json::from_str(&json)
                    .context("Redis invocation envelope is malformed")?;

                jobs.push(QueueJob {
                    stream: key.key.clone(),
                    id: id.id,
                    envelope,
                });
            }
        }

        Ok(jobs)
    }

    pub async fn finish(&self, job: QueueJob, result: &OwnedResultEnvelope) -> Result<()> {
        let mut connection = self.client.get_multiplexed_async_connection().await?;
        let json = serde_json::to_string(result)?;

        let _: String = redis::cmd("XADD")
            .arg(&self.config.result_stream)
            .arg("MAXLEN")
            .arg("~")
            .arg(self.config.result_stream_max_len)
            .arg("*")
            .arg("json")
            .arg(json)
            .query_async(&mut connection)
            .await?;

        let _: i64 = redis::cmd("XACK")
            .arg(&job.stream)
            .arg(&self.config.consumer_group)
            .arg(&job.id)
            .query_async(&mut connection)
            .await?;

        let _: i64 = redis::cmd("XDEL")
            .arg(&job.stream)
            .arg(&job.id)
            .query_async(&mut connection)
            .await?;

        debug!(stream = %job.stream, id = %job.id, "Rune invocation completed");
        Ok(())
    }

    pub async fn backlog(&self, language: RuneLanguage) -> Result<usize> {
        let stream = self.config.invocation_stream(language);
        let mut connection = self.client.get_multiplexed_async_connection().await?;
        let len: usize = connection.xlen(stream).await?;
        Ok(len)
    }

    pub async fn backlog_for_runtime(&self, runtime: InvocationRuntime) -> Result<usize> {
        let mut total = 0;
        for language in RuneLanguage::ALL {
            if language.invocation_runtime() == runtime {
                total += self.backlog(language).await?;
            }
        }
        Ok(total)
    }
}
