use std::{
    path::{Path, PathBuf},
    process::Stdio,
    sync::Arc,
    time::Duration,
};

use anyhow::{Context, Result, bail};
use serde_json::json;
use sha2::{Digest, Sha256};
use tokio::{
    fs,
    io::{AsyncBufReadExt, AsyncReadExt, AsyncWriteExt, BufReader},
    net::UnixStream,
    process::{Child, Command},
};
use uuid::Uuid;

use crate::{
    config::Config,
    protocol::{BuiltRuneArtifact, GuestResult, InvocationEnvelope, MAX_ARTIFACT_BYTES},
};

const GUEST_VSOCK_PORT: u32 = 5000;
const MAX_GUEST_RESPONSE: usize = 256 * 1024;
const MAX_API_ERROR_RESPONSE: u64 = 16 * 1024;

pub async fn load_artifact(root: &Path, artifact: &BuiltRuneArtifact) -> Result<Vec<u8>> {
    let hex = artifact
        .id
        .strip_prefix("sha256:")
        .context("artifact id is not content-addressed")?;
    let bytes = fs::read(root.join(hex)).await.context("artifact is missing")?;
    if bytes.len() as u64 != artifact.size_bytes || bytes.len() as u64 > MAX_ARTIFACT_BYTES {
        bail!("artifact size does not match descriptor");
    }
    let digest = format!("sha256:{:x}", Sha256::digest(&bytes));
    if digest != artifact.digest {
        bail!("artifact digest does not match descriptor");
    }
    Ok(bytes)
}

pub struct WarmVm {
    child: Child,
    runtime_dir: PathBuf,
    vsock_path: PathBuf,
    invocation_timeout: Duration,
}

impl WarmVm {
    pub async fn restore(config: Arc<Config>) -> Result<Self> {
        fs::create_dir_all(config.runtime_root()).await?;
        let runtime_dir = config.runtime_root().join(Uuid::new_v4().to_string());
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
            .with_context(|| format!("failed to start {}", config.firecracker_binary.display()))?;

        if let Err(error) = wait_for_path(&api_path, config.restore_timeout).await {
            let _ = child.kill().await;
            let _ = fs::remove_dir_all(&runtime_dir).await;
            return Err(error);
        }

        let request = json!({
            "snapshot_path": config.snapshot_path(),
            "mem_backend": {
                "backend_type": "File",
                "backend_path": config.memory_path()
            },
            "resume_vm": true,
            "vsock_override": { "uds_path": vsock_path }
        });

        if let Err(error) = api_put(&api_path, "/snapshot/load", &request, config.restore_timeout).await {
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

    pub async fn invoke(&mut self, envelope: &InvocationEnvelope, artifact: &[u8]) -> Result<GuestResult> {
        tokio::time::timeout(self.invocation_timeout, self.invoke_inner(envelope, artifact))
            .await
            .context("Rune invocation timed out; disposable microVM will be destroyed")?
    }

    pub async fn destroy(mut self) {
        let _ = self.child.kill().await;
        let _ = self.child.wait().await;
        let _ = fs::remove_dir_all(&self.runtime_dir).await;
    }

    async fn invoke_inner(&mut self, envelope: &InvocationEnvelope, artifact: &[u8]) -> Result<GuestResult> {
        let mut stream = UnixStream::connect(&self.vsock_path)
            .await
            .context("failed to connect to Firecracker vsock UDS")?;
        stream.write_all(format!("CONNECT {GUEST_VSOCK_PORT}\n").as_bytes()).await?;

        let mut reader = BufReader::new(stream);
        let mut handshake = String::new();
        reader.read_line(&mut handshake).await?;
        if !handshake.starts_with("OK ") {
            bail!("Firecracker vsock handshake failed: {}", handshake.trim());
        }

        let mut stream = reader.into_inner();
        let payload = serde_json::to_vec(envelope)?;
        let payload_len: u32 = payload.len().try_into().context("invocation envelope is too large")?;
        stream.write_all(&(artifact.len() as u64).to_be_bytes()).await?;
        stream.write_all(artifact).await?;
        stream.write_all(&payload_len.to_be_bytes()).await?;
        stream.write_all(&payload).await?;
        stream.flush().await?;

        let reader = BufReader::new(stream);
        let mut response = Vec::new();
        reader.take((MAX_GUEST_RESPONSE + 1) as u64).read_until(b'\n', &mut response).await?;
        if response.len() > MAX_GUEST_RESPONSE {
            bail!("guest response exceeded {MAX_GUEST_RESPONSE} bytes");
        }
        if response.last() == Some(&b'\n') {
            response.pop();
        }
        serde_json::from_slice(&response).context("guest returned malformed JSON")
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

async fn api_put(socket_path: &PathBuf, route: &str, body: &serde_json::Value, timeout: Duration) -> Result<()> {
    tokio::time::timeout(timeout, async {
        let mut stream = UnixStream::connect(socket_path)
            .await
            .with_context(|| format!("failed to connect to Firecracker API socket for {route}"))?;
        let body = serde_json::to_vec(body)?;
        let headers = format!(
            "PUT {route} HTTP/1.1\r\nHost: localhost\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n\r\n",
            body.len()
        );
        stream.write_all(headers.as_bytes()).await?;
        stream.write_all(&body).await?;
        stream.flush().await?;

        let mut reader = BufReader::new(stream);
        let mut status_line = String::new();
        reader.read_line(&mut status_line).await?;
        if status_line.is_empty() {
            bail!("Firecracker API {route} closed without an HTTP response");
        }
        if !status_line.starts_with("HTTP/1.1 200 ") && !status_line.starts_with("HTTP/1.1 204 ") {
            let mut remainder = Vec::new();
            let _ = reader.take(MAX_API_ERROR_RESPONSE).read_to_end(&mut remainder).await;
            bail!(
                "Firecracker API {route} failed: {}{}",
                status_line.trim(),
                if remainder.is_empty() {
                    String::new()
                } else {
                    format!("\n{}", String::from_utf8_lossy(&remainder).trim())
                }
            );
        }
        Ok::<(), anyhow::Error>(())
    })
    .await
    .with_context(|| format!("Firecracker API {route} timed out"))??;
    Ok(())
}

#[cfg(test)]
mod tests {
    use std::time::Duration;
    use serde_json::json;
    use tokio::{io::{AsyncReadExt, AsyncWriteExt}, net::UnixListener};
    use super::*;

    #[tokio::test]
    async fn artifact_loader_verifies_size_and_digest() {
        let dir = std::env::temp_dir().join(format!("rune-artifact-test-{}", Uuid::new_v4()));
        fs::create_dir_all(&dir).await.unwrap();
        fs::write(dir.join("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"), b"abc").await.unwrap();
        let artifact = BuiltRuneArtifact {
            id: "sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad".into(),
            digest: "sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad".into(),
            entrypoint: "rune".into(),
            size_bytes: 3,
        };
        assert_eq!(load_artifact(&dir, &artifact).await.unwrap(), b"abc");
        let mut wrong = artifact.clone();
        wrong.size_bytes = 2;
        assert!(load_artifact(&dir, &wrong).await.is_err());
        fs::remove_dir_all(&dir).await.unwrap();
    }

    #[tokio::test]
    async fn api_put_completes_from_success_status_without_waiting_for_eof() {
        let dir = std::env::temp_dir().join(format!("rune-api-test-{}", Uuid::new_v4()));
        fs::create_dir_all(&dir).await.unwrap();
        let socket = dir.join("firecracker.sock");
        let listener = UnixListener::bind(&socket).unwrap();
        let server = tokio::spawn(async move {
            let (mut stream, _) = listener.accept().await.unwrap();
            let mut request = [0_u8; 4096];
            let _ = stream.read(&mut request).await.unwrap();
            stream.write_all(b"HTTP/1.1 204 No Content\r\nConnection: keep-alive\r\n\r\n").await.unwrap();
            stream.flush().await.unwrap();
            tokio::time::sleep(Duration::from_millis(250)).await;
        });
        api_put(&socket, "/snapshot/load", &json!({ "resume_vm": true }), Duration::from_millis(100)).await.unwrap();
        server.await.unwrap();
        fs::remove_dir_all(&dir).await.unwrap();
    }

    #[tokio::test]
    async fn api_put_surfaces_firecracker_status_and_error_body() {
        let dir = std::env::temp_dir().join(format!("rune-api-test-{}", Uuid::new_v4()));
        fs::create_dir_all(&dir).await.unwrap();
        let socket = dir.join("firecracker.sock");
        let listener = UnixListener::bind(&socket).unwrap();
        let server = tokio::spawn(async move {
            let (mut stream, _) = listener.accept().await.unwrap();
            let mut request = [0_u8; 4096];
            let _ = stream.read(&mut request).await.unwrap();
            stream.write_all(b"HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\ninvalid snapshot").await.unwrap();
        });
        let error = api_put(&socket, "/snapshot/load", &json!({ "resume_vm": true }), Duration::from_millis(500)).await.unwrap_err().to_string();
        assert!(error.contains("400 Bad Request"));
        assert!(error.contains("invalid snapshot"));
        server.await.unwrap();
        fs::remove_dir_all(&dir).await.unwrap();
    }
}
