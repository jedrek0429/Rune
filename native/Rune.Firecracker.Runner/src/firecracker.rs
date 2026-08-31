use std::{path::{Path, PathBuf}, process::Stdio, sync::Arc, time::Duration};
use anyhow::{Context, Result, bail};
use serde_json::json;
use sha2::{Digest, Sha256};
use tokio::{fs, io::{AsyncBufReadExt, AsyncReadExt, AsyncWriteExt, BufReader}, net::UnixStream, process::{Child, Command}};
use uuid::Uuid;
use crate::{config::Config, protocol::{BuiltRuneArtifact, GuestResult, InvocationEnvelope, MAX_ARTIFACT_BYTES}};

const GUEST_VSOCK_PORT: u32 = 5000;
const MAX_GUEST_RESPONSE: usize = 256 * 1024;

pub async fn load_artifact(root: &Path, artifact: &BuiltRuneArtifact) -> Result<Vec<u8>> {
    let hex = artifact.id.strip_prefix("sha256:").context("artifact id is not content-addressed")?;
    let bytes = fs::read(root.join(hex)).await.context("artifact is missing")?;
    if bytes.len() as u64 != artifact.size_bytes || bytes.len() as u64 > MAX_ARTIFACT_BYTES { bail!("artifact size does not match descriptor"); }
    if format!("sha256:{:x}", Sha256::digest(&bytes)) != artifact.digest { bail!("artifact digest does not match descriptor"); }
    Ok(bytes)
}

pub struct WarmVm { child: Child, runtime_dir: PathBuf, vsock_path: PathBuf, invocation_timeout: Duration }

impl WarmVm {
    pub async fn restore(config: Arc<Config>) -> Result<Self> {
        fs::create_dir_all(config.runtime_root()).await?;
        let runtime_dir = config.runtime_root().join(Uuid::new_v4().to_string());
        fs::create_dir_all(&runtime_dir).await?;
        let api_path = runtime_dir.join("firecracker.sock");
        let vsock_path = runtime_dir.join("vsock.sock");
        let mut child = Command::new(&config.firecracker_binary).arg("--api-sock").arg(&api_path).stdin(Stdio::null()).stdout(Stdio::null()).stderr(Stdio::null()).kill_on_drop(true).spawn()?;
        if let Err(e) = wait_for_path(&api_path, config.restore_timeout).await { let _=child.kill().await; return Err(e); }
        api_put(&api_path, "/snapshot/load", &json!({
            "snapshot_path": config.snapshot_path(),
            "mem_backend": { "backend_type": "File", "backend_path": config.memory_path() },
            "resume_vm": true,
            "vsock_override": { "uds_path": vsock_path }
        }), config.restore_timeout).await?;
        wait_for_path(&vsock_path, config.restore_timeout).await?;
        Ok(Self { child, runtime_dir, vsock_path, invocation_timeout: config.invocation_timeout })
    }

    pub async fn invoke(&mut self, envelope: &InvocationEnvelope, artifact: &[u8]) -> Result<GuestResult> {
        tokio::time::timeout(self.invocation_timeout, self.invoke_inner(envelope, artifact)).await.context("Rune invocation timed out")?
    }
    async fn invoke_inner(&mut self, envelope: &InvocationEnvelope, artifact: &[u8]) -> Result<GuestResult> {
        let mut stream = UnixStream::connect(&self.vsock_path).await?;
        stream.write_all(format!("CONNECT {GUEST_VSOCK_PORT}\n").as_bytes()).await?;
        let mut reader = BufReader::new(stream);
        let mut handshake = String::new(); reader.read_line(&mut handshake).await?;
        if !handshake.starts_with("OK ") { bail!("vsock handshake failed: {}", handshake.trim()); }
        let mut stream = reader.into_inner();
        let payload = serde_json::to_vec(envelope)?;
        let payload_len: u32 = payload.len().try_into().context("invocation envelope is too large")?;
        stream.write_all(&(artifact.len() as u64).to_be_bytes()).await?;
        stream.write_all(artifact).await?; stream.write_all(&payload_len.to_be_bytes()).await?; stream.write_all(&payload).await?; stream.flush().await?;
        let reader = BufReader::new(stream); let mut response = Vec::new();
        reader.take((MAX_GUEST_RESPONSE + 1) as u64).read_until(b'\n', &mut response).await?;
        if response.len() > MAX_GUEST_RESPONSE { bail!("guest response exceeded limit"); }
        if response.last() == Some(&b'\n') { response.pop(); }
        if response.is_empty() { bail!("guest returned no response"); }
        serde_json::from_slice(&response).context("guest returned malformed JSON")
    }
    pub async fn destroy(mut self) { let _=self.child.kill().await; let _=self.child.wait().await; let _=fs::remove_dir_all(&self.runtime_dir).await; }
}

async fn wait_for_path(path: &PathBuf, timeout: Duration) -> Result<()> {
    tokio::time::timeout(timeout, async { loop { if fs::try_exists(path).await? { return Ok::<(), anyhow::Error>(()); } tokio::time::sleep(Duration::from_millis(5)).await; } }).await.context("timed out waiting for Firecracker path")??; Ok(())
}
async fn api_put(socket: &PathBuf, route: &str, body: &serde_json::Value, timeout: Duration) -> Result<()> {
    tokio::time::timeout(timeout, async {
        let mut stream=UnixStream::connect(socket).await?; let body=serde_json::to_vec(body)?;
        let headers=format!("PUT {route} HTTP/1.1\r\nHost: localhost\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n\r\n", body.len());
        stream.write_all(headers.as_bytes()).await?; stream.write_all(&body).await?; stream.flush().await?;
        let mut reader=BufReader::new(stream); let mut status=String::new(); reader.read_line(&mut status).await?;
        if !status.starts_with("HTTP/1.1 200 ") && !status.starts_with("HTTP/1.1 204 ") { bail!("Firecracker API {route} failed: {}", status.trim()); }
        Ok::<(), anyhow::Error>(())
    }).await.context("Firecracker API timed out")??; Ok(())
}
