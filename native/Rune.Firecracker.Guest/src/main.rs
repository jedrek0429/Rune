use std::{
    ffi::CString,
    fs::{self, File},
    io::{Read, Write},
    mem::size_of,
    os::{fd::FromRawFd, unix::fs::PermissionsExt},
    process::{Command, Stdio},
};

use anyhow::{Context, Result, bail};

const VSOCK_PORT: u32 = 5000;
const VMADDR_CID_ANY: u32 = u32::MAX;
const MAX_ARTIFACT_BYTES: usize = 16 * 1024 * 1024;
const MAX_REQUEST_BYTES: usize = 128 * 1024;
const MAX_RESPONSE_BYTES: usize = 256 * 1024;
const PID_LIMIT: libc::rlim_t = 32;
const FD_LIMIT: libc::rlim_t = 128;
const CPU_LIMIT_SECONDS: libc::rlim_t = 3;
const WRITABLE_TMPFS_MIB: usize = 32;
const WORKER_UID: libc::uid_t = 1000;
const WORKER_GID: libc::gid_t = 1000;
const ARTIFACT_PATH: &str = "/tmp/rune-artifact";

fn main() -> Result<()> {
    mount_guest_filesystems()?;
    chown("/tmp", WORKER_UID, WORKER_GID)?;
    apply_resource_limits()?;
    drop_privileges()?;

    let listener = VsockListener::bind(VSOCK_PORT)?;
    println!("RUNE_READY");

    let mut connection = listener.accept()?;
    if let Err(error) = invoke(&mut connection) {
        write_error(&mut connection, &error.to_string())?;
    }
    Ok(())
}

fn invoke(connection: &mut File) -> Result<()> {
    let artifact_len = read_u64(connection)? as usize;
    if artifact_len == 0 || artifact_len > MAX_ARTIFACT_BYTES {
        bail!("artifact exceeded guest limit");
    }
    let mut artifact = vec![0; artifact_len];
    connection.read_exact(&mut artifact)?;

    let request_len = read_u32(connection)? as usize;
    if request_len == 0 || request_len > MAX_REQUEST_BYTES {
        bail!("invocation envelope exceeded guest limit");
    }
    let mut request = vec![0; request_len];
    connection.read_exact(&mut request)?;

    fs::write(ARTIFACT_PATH, artifact)?;
    fs::set_permissions(ARTIFACT_PATH, fs::Permissions::from_mode(0o500))?;

    let mut child = Command::new(ARTIFACT_PATH)
        .env_clear()
        .env("PATH", "/usr/local/bin:/usr/bin:/bin")
        .env("HOME", "/tmp")
        .env("TMPDIR", "/tmp")
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::null())
        .spawn()
        .context("failed to start Rune artifact")?;

    child
        .stdin
        .take()
        .context("artifact stdin was not piped")?
        .write_all(&request)?;

    let mut response = Vec::new();
    child
        .stdout
        .take()
        .context("artifact stdout was not piped")?
        .take((MAX_RESPONSE_BYTES + 1) as u64)
        .read_to_end(&mut response)?;
    let status = child.wait()?;

    if response.len() > MAX_RESPONSE_BYTES {
        bail!("Rune response exceeded guest limit");
    }
    if !status.success() {
        bail!("Rune artifact exited with {status}");
    }
    if response.is_empty() {
        response.extend_from_slice(b"{\"actions\":[],\"error\":null}");
    }

    connection.write_all(&response)?;
    if !response.ends_with(b"\n") {
        connection.write_all(b"\n")?;
    }
    Ok(())
}

fn read_u64(reader: &mut impl Read) -> Result<u64> {
    let mut bytes = [0; 8];
    reader.read_exact(&mut bytes)?;
    Ok(u64::from_be_bytes(bytes))
}

fn read_u32(reader: &mut impl Read) -> Result<u32> {
    let mut bytes = [0; 4];
    reader.read_exact(&mut bytes)?;
    Ok(u32::from_be_bytes(bytes))
}

fn apply_resource_limits() -> Result<()> {
    set_limit(libc::RLIMIT_CPU, CPU_LIMIT_SECONDS)?;
    set_limit(libc::RLIMIT_NPROC, PID_LIMIT)?;
    set_limit(libc::RLIMIT_NOFILE, FD_LIMIT)
}

fn set_limit(resource: libc::__rlimit_resource_t, value: libc::rlim_t) -> Result<()> {
    let limit = libc::rlimit { rlim_cur: value, rlim_max: value };
    if unsafe { libc::setrlimit(resource, &limit) } != 0 {
        return Err(std::io::Error::last_os_error()).context("setrlimit failed");
    }
    Ok(())
}

fn drop_privileges() -> Result<()> {
    if unsafe { libc::setgroups(0, std::ptr::null()) } != 0 {
        return Err(std::io::Error::last_os_error()).context("setgroups failed");
    }
    if unsafe { libc::setgid(WORKER_GID) } != 0 {
        return Err(std::io::Error::last_os_error()).context("setgid failed");
    }
    if unsafe { libc::setuid(WORKER_UID) } != 0 {
        return Err(std::io::Error::last_os_error()).context("setuid failed");
    }
    Ok(())
}

fn write_error(connection: &mut File, message: &str) -> Result<()> {
    let escaped = message
        .replace('\\', "\\\\")
        .replace('"', "\\\"")
        .replace('\n', "\\n")
        .replace('\r', "\\r");
    writeln!(connection, "{{\"actions\":[],\"error\":\"{escaped}\"}}")?;
    Ok(())
}

struct VsockListener { fd: i32 }

impl VsockListener {
    fn bind(port: u32) -> Result<Self> {
        let fd = unsafe { libc::socket(libc::AF_VSOCK, libc::SOCK_STREAM | libc::SOCK_CLOEXEC, 0) };
        if fd < 0 {
            return Err(std::io::Error::last_os_error()).context("socket(AF_VSOCK) failed");
        }
        let address = libc::sockaddr_vm {
            svm_family: libc::AF_VSOCK as libc::sa_family_t,
            svm_reserved1: 0,
            svm_port: port,
            svm_cid: VMADDR_CID_ANY,
            svm_zero: [0; 4],
        };
        if unsafe {
            libc::bind(
                fd,
                &address as *const libc::sockaddr_vm as *const libc::sockaddr,
                size_of::<libc::sockaddr_vm>() as libc::socklen_t,
            )
        } != 0 {
            let error = std::io::Error::last_os_error();
            unsafe { libc::close(fd) };
            return Err(error).context("bind(AF_VSOCK) failed");
        }
        if unsafe { libc::listen(fd, 1) } != 0 {
            let error = std::io::Error::last_os_error();
            unsafe { libc::close(fd) };
            return Err(error).context("listen(AF_VSOCK) failed");
        }
        Ok(Self { fd })
    }

    fn accept(&self) -> Result<File> {
        let fd = unsafe {
            libc::accept4(
                self.fd,
                std::ptr::null_mut(),
                std::ptr::null_mut(),
                libc::SOCK_CLOEXEC,
            )
        };
        if fd < 0 {
            return Err(std::io::Error::last_os_error()).context("accept(AF_VSOCK) failed");
        }
        Ok(unsafe { File::from_raw_fd(fd) })
    }
}

impl Drop for VsockListener {
    fn drop(&mut self) {
        unsafe { libc::close(self.fd) };
    }
}

fn mount_guest_filesystems() -> Result<()> {
    fs::create_dir_all("/proc")?;
    fs::create_dir_all("/tmp")?;
    mount("proc", "/proc", "proc", libc::MS_NOSUID | libc::MS_NODEV | libc::MS_NOEXEC, "")?;
    mount(
        "tmpfs",
        "/tmp",
        "tmpfs",
        libc::MS_NOSUID | libc::MS_NODEV,
        &format!("size={WRITABLE_TMPFS_MIB}m,mode=0700"),
    )
}

fn mount(source: &str, target: &str, fstype: &str, flags: libc::c_ulong, data: &str) -> Result<()> {
    let source = CString::new(source)?;
    let target = CString::new(target)?;
    let fstype = CString::new(fstype)?;
    let data = CString::new(data)?;
    if unsafe {
        libc::mount(
            source.as_ptr(),
            target.as_ptr(),
            fstype.as_ptr(),
            flags,
            data.as_ptr().cast(),
        )
    } != 0 {
        return Err(std::io::Error::last_os_error()).context("mount failed");
    }
    Ok(())
}

fn chown(path: &str, uid: libc::uid_t, gid: libc::gid_t) -> Result<()> {
    let path = CString::new(path)?;
    if unsafe { libc::chown(path.as_ptr(), uid, gid) } != 0 {
        return Err(std::io::Error::last_os_error()).context("chown failed");
    }
    Ok(())
}
