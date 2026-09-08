use anyhow::{Context, Result, bail};
use std::{
    collections::HashMap,
    ffi::CString,
    fs,
    io::Write,
    os::unix::fs::PermissionsExt,
    path::Path,
    process::{Command, Stdio},
    thread,
};

const WORKER_UID: libc::uid_t = 1000;
const WORKER_GID: libc::gid_t = 1000;
const MAX_DIAGNOSTIC_BYTES: usize = 64 * 1024;

fn main() -> Result<()> {
    mount_proc()?;
    let cmdline = fs::read_to_string("/proc/cmdline")?;
    let values = values(&cmdline);

    if values.get("rune.cache_warm") == Some(&"scriptc") {
        return warm_scriptc_cache(&values);
    }

    fs::create_dir_all("/work")?;
    fs::create_dir_all("/input")?;
    mount(
        "/dev/vdb",
        "/work",
        "ext4",
        libc::MS_NOSUID | libc::MS_NODEV,
    )?;
    mount(
        "/dev/vdc",
        "/input",
        "ext4",
        libc::MS_RDONLY | libc::MS_NOSUID | libc::MS_NODEV | libc::MS_NOEXEC,
    )?;
    fs::set_permissions("/work", fs::Permissions::from_mode(0o700))?;
    chown("/work", WORKER_UID, WORKER_GID)?;

    set_limit(libc::RLIMIT_CPU, positive(&values, "rune.cpu_seconds")?)?;
    set_limit(libc::RLIMIT_NPROC, positive(&values, "rune.pid_limit")?)?;
    set_limit(libc::RLIMIT_NOFILE, positive(&values, "rune.fd_limit")?)?;
    let language = values
        .get("rune.language")
        .context("missing rune.language")?;

    if matches!(*language, "javascript" | "typescript") {
        fs::create_dir_all("/cache-seed")?;
        mount(
            "/dev/vdd",
            "/cache-seed",
            "ext4",
            libc::MS_RDONLY | libc::MS_NOSUID | libc::MS_NODEV | libc::MS_NOEXEC,
        )?;
    }

    drop_privileges()?;
    fs::create_dir_all("/work/tmp")?;

    let (program, args): (&str, Vec<&str>) = match *language {
        "javascript" => (
            "rune-build-scriptc",
            vec!["/input/source.js", "/work/artifact"],
        ),
        "typescript" => (
            "rune-build-scriptc",
            vec!["/input/source.ts", "/work/artifact"],
        ),
        "rust" => (
            "rustc",
            vec![
                "--edition=2024",
                "-O",
                "/input/source.rs",
                "-o",
                "/work/artifact",
            ],
        ),
        "c" => (
            "clang",
            vec!["-O2", "/input/source.c", "-o", "/work/artifact"],
        ),
        "cpp" => (
            "clang++",
            vec!["-O2", "/input/source.cpp", "-o", "/work/artifact"],
        ),
        "csharp" => (
            "dotnet",
            vec![
                "publish",
                "/input/Rune.csproj",
                "-c",
                "Release",
                "--no-restore",
                "-p:PublishAot=true",
                "-o",
                "/work/publish",
            ],
        ),
        "python" => (
            "rune-build-python",
            vec!["/input/source.py", "/work/artifact"],
        ),
        "ruby" => (
            "rune-build-ruby",
            vec!["/input/source.rb", "/work/artifact"],
        ),
        other => bail!("unsupported build language: {other}"),
    };

    let output = Command::new(program)
        .args(args)
        .env_clear()
        .env("PATH", "/usr/local/bin:/usr/bin:/bin")
        .env("HOME", "/work")
        .env("TMPDIR", "/work/tmp")
        .env("NUGET_PACKAGES", "/opt/rune/nuget")
        .stdin(Stdio::null())
        .output()
        .context("failed to start compiler")?;
    write_diagnostics(&output.stdout, &output.stderr)?;

    if !output.status.success() {
        signal_and_park("RUNE_BUILD_FAILED");
    }

    if *language == "csharp" {
        let published = fs::read_dir("/work/publish")?
            .filter_map(Result::ok)
            .map(|entry| entry.path())
            .find(|path| path.is_file() && path.extension().is_none())
            .context("Native AOT build produced no executable")?;
        fs::copy(published, "/work/artifact")?;
    }

    let artifact = Path::new("/work/artifact");
    let metadata = fs::metadata(artifact).context("compiler produced no artifact")?;
    if metadata.len() == 0 {
        bail!("compiler produced an empty artifact");
    }
    fs::set_permissions(artifact, fs::Permissions::from_mode(0o755))?;
    signal_and_park("RUNE_BUILD_DONE");
}

fn warm_scriptc_cache(values: &HashMap<&str, &str>) -> Result<()> {
    fs::create_dir_all("/work")?;
    mount(
        "/dev/vdb",
        "/work",
        "ext4",
        libc::MS_NOSUID | libc::MS_NODEV,
    )?;
    fs::set_permissions("/work", fs::Permissions::from_mode(0o700))?;
    chown("/work", WORKER_UID, WORKER_GID)?;
    set_limit(libc::RLIMIT_CPU, positive(values, "rune.wall_seconds")?)?;
    set_limit(libc::RLIMIT_NPROC, positive(values, "rune.pid_limit")?)?;
    set_limit(libc::RLIMIT_NOFILE, positive(values, "rune.fd_limit")?)?;
    drop_privileges()?;

    for profile in ["runtime", "dynamic"] {
        let status = Command::new("scriptc")
            .args(["cache", "warm", profile])
            .env_clear()
            .env("PATH", "/usr/local/bin:/usr/bin:/bin")
            .env("HOME", "/work")
            .env("TMPDIR", "/work")
            .env("SCRIPTC_CACHE_DIR", "/work")
            .current_dir("/work")
            .status()
            .with_context(|| format!("failed to start ScriptC {profile} cache warm"))?;
        if !status.success() {
            signal_and_park("RUNE_CACHE_WARM_FAILED");
        }
    }

    signal_and_park("RUNE_CACHE_WARM_DONE");
}

fn signal_and_park(marker: &str) -> ! {
    unsafe { libc::sync() };
    println!("{marker}");
    let _ = std::io::stdout().flush();
    loop {
        thread::park();
    }
}

fn values(cmdline: &str) -> HashMap<&str, &str> {
    cmdline
        .split_whitespace()
        .filter_map(|part| part.split_once('='))
        .collect()
}

fn positive(values: &HashMap<&str, &str>, key: &str) -> Result<u64> {
    let value = values
        .get(key)
        .with_context(|| format!("missing {key}"))?
        .parse::<u64>()
        .with_context(|| format!("invalid {key}"))?;
    if value == 0 {
        bail!("{key} must be positive");
    }
    Ok(value)
}

fn write_diagnostics(stdout: &[u8], stderr: &[u8]) -> Result<()> {
    let mut diagnostics = Vec::new();
    diagnostics.extend_from_slice(stdout);
    diagnostics.extend_from_slice(stderr);
    if diagnostics.len() > MAX_DIAGNOSTIC_BYTES {
        diagnostics.truncate(MAX_DIAGNOSTIC_BYTES);
        diagnostics.extend_from_slice(b"\n[compiler diagnostics truncated]\n");
    }
    fs::write("/work/diagnostics.txt", diagnostics)?;
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

fn set_limit(resource: libc::__rlimit_resource_t, value: u64) -> Result<()> {
    let limit = libc::rlimit {
        rlim_cur: value,
        rlim_max: value,
    };
    if unsafe { libc::setrlimit(resource, &limit) } != 0 {
        return Err(std::io::Error::last_os_error()).context("setrlimit failed");
    }
    Ok(())
}

fn mount_proc() -> Result<()> {
    fs::create_dir_all("/proc")?;
    mount(
        "proc",
        "/proc",
        "proc",
        libc::MS_NOSUID | libc::MS_NODEV | libc::MS_NOEXEC,
    )
}

fn mount(source: &str, target: &str, fstype: &str, flags: libc::c_ulong) -> Result<()> {
    let source = CString::new(source)?;
    let target = CString::new(target)?;
    let fstype = CString::new(fstype)?;
    if unsafe {
        libc::mount(
            source.as_ptr(),
            target.as_ptr(),
            fstype.as_ptr(),
            flags,
            std::ptr::null(),
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
