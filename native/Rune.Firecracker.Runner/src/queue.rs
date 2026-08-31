use crate::{
    config::Config,
    protocol::{InvocationEnvelope, ResultEnvelope},
};
use anyhow::{Context, Result};
use redis::{Client, FromRedisValue, RedisError, streams::StreamReadReply};
use std::sync::Arc;

#[derive(Clone)]
pub struct RedisQueue {
    client: Client,
    config: Arc<Config>,
}

#[derive(Clone)]
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
    pub async fn ensure_group(&self) -> Result<()> {
        let mut connection = self.client.get_multiplexed_async_connection().await?;
        let result: Result<String, RedisError> = redis::cmd("XGROUP")
            .arg("CREATE")
            .arg(&self.config.invocation_stream)
            .arg(&self.config.consumer_group)
            .arg("0-0")
            .arg("MKSTREAM")
            .query_async(&mut connection)
            .await;
        match result {
            Ok(_) => Ok(()),
            Err(e) if e.to_string().contains("BUSYGROUP") => Ok(()),
            Err(e) => Err(e.into()),
        }
    }
    pub async fn read(&self) -> Result<Vec<QueueJob>> {
        let mut connection = self.client.get_multiplexed_async_connection().await?;
        let reply: StreamReadReply = redis::cmd("XREADGROUP")
            .arg("GROUP")
            .arg(&self.config.consumer_group)
            .arg(&self.config.consumer_name)
            .arg("COUNT")
            .arg(self.config.read_batch_size)
            .arg("BLOCK")
            .arg(1000)
            .arg("STREAMS")
            .arg(&self.config.invocation_stream)
            .arg(">")
            .query_async(&mut connection)
            .await?;
        let mut jobs = Vec::new();
        for key in reply.keys {
            for id in key.ids {
                let Some(value) = id.map.get("json") else {
                    continue;
                };
                let json =
                    String::from_redis_value(value).context("invocation JSON is not UTF-8")?;
                jobs.push(QueueJob {
                    stream: key.key.clone(),
                    id: id.id,
                    envelope: serde_json::from_str(&json)
                        .context("malformed invocation envelope")?,
                });
            }
        }
        Ok(jobs)
    }
    pub async fn finish(&self, job: QueueJob, result: &ResultEnvelope) -> Result<()> {
        let mut connection = self.client.get_multiplexed_async_connection().await?;
        let json = serde_json::to_string(result)?;
        let _: String = redis::cmd("XADD")
            .arg(&self.config.result_stream)
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
        Ok(())
    }
    pub async fn backlog(&self) -> Result<usize> {
        let mut connection = self.client.get_multiplexed_async_connection().await?;
        Ok(redis::cmd("XLEN")
            .arg(&self.config.invocation_stream)
            .query_async(&mut connection)
            .await?)
    }
}
