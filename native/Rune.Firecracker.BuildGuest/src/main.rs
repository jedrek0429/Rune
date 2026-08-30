use std::{
    collections::HashMap,
    ffi::CString,
    fs,
    io::Read,
    os::unix::fs::PermissionsExt,
    process::{Command, Stdio},
    thread,
};

use anyhow::{Context, Result, bail};

const WORKER_UID: libc::uid_t = 1000;
const WORKER_GID: libc::gid_t = 1000;
const MAX_DIAGNOSTIC_BYTES: usize = 64 * 1024;

#[derive(Debug, Clone, PartialEq, Eq)]
struct BuildPolicy {
    pool: String,
    pid_limit: u64,
    fd_limit: u64,
    cpu_seconds: u64,
}

fn parse_policy(cmdline: &str) -> Result<BuildPolicy> {
    let values: HashMap<_, _> = cmdline
        .split_whitespace()
        .filter_map(|part| part.split_once('='))
        .collect();

    let pool = values
        .get("rune.build_pool")
        .context("missing rune.build_pool")?
        .to_string();
    if !matches!(
        pool.as_str(),
        "scriptc" | "clang" | "rust" | "dotnet-aot" | "python" | "ruby"
    ) {
        bail!("unsupported build pool {pool}");
    }

    let pid_limit = parse_positive(&values, "rune.pid_limit")?;
    let fd_limit = parse_positive(&values, "rune.fd_limit")?;
    let cpu_seconds = parse_positive(&values, "rune.wall_seconds")?;

    Ok(BuildPolicy {
        pool,
        pid_limit,
        fd_limit,
        cpu_seconds,
    })
}

fn parse_positive(values: &HashMap<&str, &str>, key: &str) -> Result<u64> {
    let value = values
        .get(key)
        .with_context(|| format!("missing {key}"))?
        .parse::<u64>()
        .with_context(|| format!("invalid {key}"))?;
    if value == 0 {
        bail!("{key} must be greater than zero");
    }
    Ok(value)
}

fn main() -> Result<()> {
    mount_guest_filesystems()?;
    let cmdline =
        fs::read_to_string("/proc/cmdline").context("failed to read kernel command line")?;
    let policy = parse_policy(&cmdline)?;

    fs::create_dir_all("/work")?;
    mount(
        "/dev/vdb",
        "/work",
        "ext4",
        libc::MS_NOSUID | libc::MS_NODEV,
        "",
    )
    .context("failed to mount bounded build scratch disk")?;
    fs::set_permissions("/work", fs::Permissions::from_mode(0o700))?;
    chown("/work", WORKER_UID, WORKER_GID)?;

    set_limit(libc::RLIMIT_CPU, policy.cpu_seconds)?;
    set_limit(libc::RLIMIT_NPROC, policy.pid_limit)?;
    set_limit(libc::RLIMIT_NOFILE, policy.fd_limit)?;

    drop_privileges()?;
    run_worker(&policy.pool)
}

fn run_worker(pool: &str) -> Result<()> {
    let mut child = Command::new("/opt/rune/build-worker")
        .arg(pool)
        .env_clear()
        .env("PATH", "/usr/local/bin:/usr/bin:/bin")
        .env("HOME", "/work")
        .env("TMPDIR", "/work/tmp")
        .current_dir("/work")
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .context("failed to start build worker")?;

    let stdout = child.stdout.take().context("build stdout was not piped")?;
    let stderr = child.stderr.take().context("build stderr was not piped")?;
    let stdout_thread = thread::spawn(move || drain_bounded(stdout));
    let stderr_thread = thread::spawn(move || drain_bounded(stderr));

    let status = child.wait().context("failed waiting for build worker")?;
    let mut diagnostics = stdout_thread.join().unwrap_or_default();
    diagnostics.extend(stderr_thread.join().unwrap_or_default());
    diagnostics.truncate(MAX_DIAGNOSTIC_BYTES);
    fs::write("/work/diagnostics.txt", diagnostics)?;

    if status.success() {
        println!("RUNE_BUILD_DONE");
        Ok(())
    } else {
        println!("RUNE_BUILD_FAILED");
        bail!("build worker exited with {status}")
    }
}

fn drain_bounded(mut reader: impl Read) -> Vec<u8> {
    let mut kept = Vec::with_capacity(MAX_DIAGNOSTIC_BYTES);
    let mut buffer = [0_u8; 8192];
    loop {
        let Ok(read) = reader.read(&mut buffer) else {
            break;
        };
        if read == 0 {
            break;
        }
        let remaining = MAX_DIAGNOSTIC_BYTES.saturating_sub(kept.len());
        kept.extend_from_slice(&buffer[..read.min(remaining)]);
    }
    kept
}

fn set_limit(resource: libc::__rlimit_resource_t, value: u64) -> Result<()> {
    let value: libc::rlim_t = value;
    let limit = libc::rlimit {
        rlim_cur: value,
        rlim_max: value,
    };
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

fn mount_guest_filesystems() -> Result<()> {
    fs::create_dir_all("/dev")?;
    fs::create_dir_all("/proc")?;
    mount(
        "devtmpfs",
        "/dev",
        "devtmpfs",
        libc::MS_NOSUID,
        "mode=0755",
    )?;
    mount(
        "proc",
        "/proc",
        "proc",
        libc::MS_NOSUID | libc::MS_NODEV | libc::MS_NOEXEC,
        "",
    )?;
    Ok(())
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
    } != 0
    {
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

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_build_limits_from_kernel_command_line() {
        let policy = parse_policy(
            "root=/dev/vda ro rune.build_pool=dotnet-aot rune.pid_limit=128 rune.fd_limit=256 rune.wall_seconds=60",
        )
        .unwrap();

        assert_eq!(
            policy,
            BuildPolicy {
                pool: "dotnet-aot".into(),
                pid_limit: 128,
                fd_limit: 256,
                cpu_seconds: 60,
            }
        );
    }

    #[test]
    fn rejects_unknown_build_pool() {
        let error = parse_policy(
            "rune.build_pool=node rune.pid_limit=128 rune.fd_limit=256 rune.wall_seconds=30",
        )
        .unwrap_err()
        .to_string();
        assert!(error.contains("unsupported build pool"));
    }

    #[test]
    fn rejects_missing_or_zero_limits() {
        assert!(
            parse_policy(
                "rune.build_pool=rust rune.pid_limit=0 rune.fd_limit=256 rune.wall_seconds=45"
            )
            .is_err()
        );
        assert!(
            parse_policy("rune.build_pool=rust rune.pid_limit=128 rune.wall_seconds=45").is_err()
        );
    }

    #[test]
    fn diagnostics_are_capped_while_stream_is_fully_drained() {
        let input = vec![b'x'; MAX_DIAGNOSTIC_BYTES * 2];
        let kept = drain_bounded(input.as_slice());
        assert_eq!(kept.len(), MAX_DIAGNOSTIC_BYTES);
    }
}
