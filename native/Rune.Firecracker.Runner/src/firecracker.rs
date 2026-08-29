use std::{path::PathBuf, process::Stdio, sync::Arc, time::Duration};

use anyhow::{Context, Result, bail};
use serde_json::json;
use tokio::{
    fs,
    io::{AsyncBufReadExt, AsyncReadExt, AsyncWriteExt, BufReader},
    net::UnixStream,
    process::{Child, Command},
};
use uuid::Uuid;

use crate::{
    config::Config,
    protocol::{GuestResult, InvocationEnvelope, RuneLanguage},
};

const GUEST_VSOCK_PORT: u32 = 5000;
const MAX_GUEST_RESPONSE: usize = 256 * 1024;

pub struct WarmVm {
    child: Child,
    runtime_dir: PathBuf,
    vsock_path: PathBuf,
    invocation_timeout: Duration,
}

impl WarmVm {
    pub async fn restore(language: RuneLanguage, config: Arc<Config>) -> Result<Self> {
        fs::create_dir_all(config.runtime_root()).await?;

        let runtime_dir = config
            .runtime_root()
            .join(format!("{}-{}", language.as_str(), Uuid::new_v4()));
        fs::create_dir_all(&runtime_dir).await?;

        let api_path = runtime_dir.join("firecracker.sock");
        let vsock_path = runtime_dir.join("vsock.sock");

        let mut child = Command::new(&config.firecracker_binary)
            .arg("--api-sock")
            .arg(&api_path)
            .stdin(Stdio::null())
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .kill_on_drop(true)
            .spawn()
            .with_context(|| {
                format!(
                    "failed to start {}",
                    config.firecracker_binary.display()
                )
            })?;

        if let Err(error) = wait_for_path(&api_path, config.restore_timeout).await {
            let _ = child.kill().await;
            let _ = fs::remove_dir_all(&runtime_dir).await;
            return Err(error);
        }

        let request = json!({
            "snapshot_path": config.snapshot_path(language),
            "mem_backend": {
                "backend_type": "File",
                "backend_path": config.memory_path(language)
            },
            "resume_vm": true,
            "vsock_override": {
                "uds_path": vsock_path
            }
        });

        if let Err(error) = api_put(
            &api_path,
            "/snapshot/load",
            &request,
            config.restore_timeout,
        )
        .await
        {
            let _ = child.kill().await;
            let _ = fs::remove_dir_all(&runtime_dir).await;
            return Err(error);
        }

        if let Err(error) = wait_for_path(&vsock_path, config.restore_timeout).await {
            let _ = child.kill().await;
            let _ = fs::remove_dir_all(&runtime_dir).await;
            return Err(error);
        }

        Ok(Self {
            child,
            runtime_dir,
            vsock_path,
            invocation_timeout: config.invocation_timeout,
        })
    }

    pub async fn invoke(&mut self, envelope: &InvocationEnvelope) -> Result<GuestResult> {
        tokio::time::timeout(self.invocation_timeout, self.invoke_inner(envelope))
            .await
            .context("Rune invocation timed out; disposable microVM will be destroyed")?
    }

    pub async fn destroy(mut self) {
        let _ = self.child.kill().await;
        let _ = self.child.wait().await;
        let _ = fs::remove_dir_all(&self.runtime_dir).await;
    }

    async fn invoke_inner(&mut self, envelope: &InvocationEnvelope) -> Result<GuestResult> {
        let mut stream = UnixStream::connect(&self.vsock_path)
            .await
            .context("failed to connect to Firecracker vsock UDS")?;

        stream
            .write_all(format!("CONNECT {GUEST_VSOCK_PORT}\n").as_bytes())
            .await?;

        let mut reader = BufReader::new(stream);
        let mut handshake = String::new();
        reader.read_line(&mut handshake).await?;

        if !handshake.starts_with("OK ") {
            bail!("Firecracker vsock handshake failed: {}", handshake.trim());
        }

        let mut stream = reader.into_inner();
        let payload = serde_json::to_vec(envelope)?;
        stream.write_all(&payload).await?;
        stream.write_all(b"\n").await?;
        stream.flush().await?;

        let reader = BufReader::new(stream);
        let mut response = Vec::new();
        reader
            .take((MAX_GUEST_RESPONSE + 1) as u64)
            .read_until(b'\n', &mut response)
            .await?;

        if response.len() > MAX_GUEST_RESPONSE {
            bail!("guest response exceeded {MAX_GUEST_RESPONSE} bytes");
        }
        if response.last() == Some(&b'\n') {
            response.pop();
        }

        serde_json::from_slice(&response)
            .context("guest returned malformed JSON")
    }
}

async fn wait_for_path(path: &PathBuf, timeout: Duration) -> Result<()> {
    tokio::time::timeout(timeout, async {
        loop {
            if fs::try_exists(path).await? {
                return Ok::<(), anyhow::Error>(());
            }
            tokio::time::sleep(Duration::from_millis(5)).await;
        }
    })
    .await
    .with_context(|| format!("timed out waiting for {}", path.display()))??;

    Ok(())
}

async fn api_put(
    socket_path: &PathBuf,
    route: &str,
    body: &serde_json::Value,
    timeout: Duration,
) -> Result<()> {
    tokio::time::timeout(timeout, async {
        let mut stream = UnixStream::connect(socket_path).await?;
        let body = serde_json::to_vec(body)?;
        let headers = format!(
            "PUT {route} HTTP/1.1\r\nHost: localhost\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n\r\n",
            body.len()
        );

        stream.write_all(headers.as_bytes()).await?;
        stream.write_all(&body).await?;
        stream.shutdown().await?;

        let mut response = Vec::new();
        stream.read_to_end(&mut response).await?;
        let status = String::from_utf8_lossy(&response);

        if !(status.starts_with("HTTP/1.1 200") || status.starts_with("HTTP/1.1 204")) {
            bail!("Firecracker API {route} failed: {status}");
        }

        Ok::<(), anyhow::Error>(())
    })
    .await
    .with_context(|| format!("Firecracker API {route} timed out"))??;

    Ok(())
}
