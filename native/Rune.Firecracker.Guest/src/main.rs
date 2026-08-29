use std::{
    ffi::CString,
    fs::File,
    io::{BufRead, BufReader, Read, Write},
    mem::size_of,
    os::fd::FromRawFd,
    process::{Child, ChildStdin, ChildStdout, Command, Stdio},
};

use anyhow::{Context, Result, bail};

const VSOCK_PORT: u32 = 5000;
const VMADDR_CID_ANY: u32 = u32::MAX;
const MAX_REQUEST_BYTES: usize = 128 * 1024;
const MAX_RESPONSE_BYTES: usize = 256 * 1024;

fn main() -> Result<()> {
    mount_guest_filesystems();

    let language = std::fs::read_to_string("/etc/rune-language")
        .context("missing /etc/rune-language")?
        .trim()
        .to_owned();

    let mut worker = Worker::start(&language)?;
    let listener = VsockListener::bind(VSOCK_PORT)?;

    println!("RUNE_READY {language}");

    loop {
        let mut connection = listener.accept()?;
        let mut request = String::new();

        BufReader::new(connection.try_clone()?)
            .take(MAX_REQUEST_BYTES as u64 + 1)
            .read_line(&mut request)?;

        if request.len() > MAX_REQUEST_BYTES {
            write_error(&mut connection, "invocation envelope exceeded guest limit")?;
            continue;
        }

        match worker.invoke(&request) {
            Ok(response) if response.len() <= MAX_RESPONSE_BYTES => {
                connection.write_all(response.as_bytes())?;
                connection.write_all(b"\n")?;
            }
            Ok(_) => {
                write_error(&mut connection, "worker response exceeded guest limit")?;
            }
            Err(error) => {
                write_error(&mut connection, &format!("guest worker failed: {error}"))?;
            }
        }
    }
}

fn write_error(connection: &mut File, message: &str) -> Result<()> {
    let escaped = message
        .replace('\\', "\\\\")
        .replace('"', "\\\"")
        .replace('\n', "\\n")
        .replace('\r', "\\r");
    writeln!(
        connection,
        "{{\"actions\":[],\"error\":\"{escaped}\",\"durationMicros\":0}}"
    )?;
    Ok(())
}

struct Worker {
    _child: Child,
    stdin: ChildStdin,
    stdout: BufReader<ChildStdout>,
}

impl Worker {
    fn start(language: &str) -> Result<Self> {
        let mut command = match language {
            "javascript" => {
                let mut command = Command::new("node");
                command.arg("/opt/rune/worker.mjs");
                command
            }
            "python" => {
                let mut command = Command::new("python3");
                command.arg("-u").arg("/opt/rune/worker.py");
                command
            }
            "rust" => {
                let mut command = Command::new("python3");
                command.arg("-u").arg("/opt/rune/worker-rust.py");
                command
            }
            other => bail!("unsupported guest language {other}"),
        };

        command
            .env("PATH", "/usr/local/cargo/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin")
            .env("HOME", "/root")
            .env("TMPDIR", "/tmp")
            .env("RUSTUP_HOME", "/usr/local/rustup")
            .env("CARGO_HOME", "/usr/local/cargo");

        let mut child = command
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::inherit())
            .spawn()
            .context("failed to start language worker")?;

        let stdin = child.stdin.take().context("worker stdin was not piped")?;
        let stdout = child.stdout.take().context("worker stdout was not piped")?;
        let mut stdout = BufReader::new(stdout);
        let mut ready = String::new();
        stdout.read_line(&mut ready)?;

        if ready.trim() != "{\"ready\":true}" {
            bail!("language worker did not become ready: {}", ready.trim());
        }

        Ok(Self {
            _child: child,
            stdin,
            stdout,
        })
    }

    fn invoke(&mut self, request: &str) -> Result<String> {
        self.stdin.write_all(request.as_bytes())?;
        if !request.ends_with('\n') {
            self.stdin.write_all(b"\n")?;
        }
        self.stdin.flush()?;

        let mut response = String::new();
        self.stdout
            .by_ref()
            .take(MAX_RESPONSE_BYTES as u64 + 1)
            .read_line(&mut response)?;

        if response.len() > MAX_RESPONSE_BYTES {
            bail!("worker response exceeded limit");
        }

        Ok(response.trim_end_matches(['\r', '\n']).to_owned())
    }
}

struct VsockListener {
    fd: i32,
}

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

        let result = unsafe {
            libc::bind(
                fd,
                &address as *const libc::sockaddr_vm as *const libc::sockaddr,
                size_of::<libc::sockaddr_vm>() as libc::socklen_t,
            )
        };

        if result != 0 {
            let error = std::io::Error::last_os_error();
            unsafe { libc::close(fd) };
            return Err(error).context("bind(AF_VSOCK) failed");
        }

        if unsafe { libc::listen(fd, 16) } != 0 {
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

fn mount_guest_filesystems() {
    let _ = mount(
        "devtmpfs",
        "/dev",
        "devtmpfs",
        libc::MS_NOSUID,
        "mode=0755",
    );
    let _ = mount(
        "proc",
        "/proc",
        "proc",
        libc::MS_NOSUID | libc::MS_NODEV | libc::MS_NOEXEC,
        "",
    );
    let _ = mount(
        "tmpfs",
        "/tmp",
        "tmpfs",
        libc::MS_NOSUID | libc::MS_NODEV,
        "size=384m",
    );
}

fn mount(
    source: &str,
    target: &str,
    fstype: &str,
    flags: libc::c_ulong,
    data: &str,
) -> Result<()> {
    let source = CString::new(source)?;
    let target = CString::new(target)?;
    let fstype = CString::new(fstype)?;
    let data = CString::new(data)?;

    let result = unsafe {
        libc::mount(
            source.as_ptr(),
            target.as_ptr(),
            fstype.as_ptr(),
            flags,
            data.as_ptr().cast(),
        )
    };

    if result != 0 {
        return Err(std::io::Error::last_os_error()).context("mount failed");
    }

    Ok(())
}
