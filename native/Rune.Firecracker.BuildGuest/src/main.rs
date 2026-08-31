use std::{
    collections::HashMap,
    ffi::CString,
    fs,
    io::{Read, Write},
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
    language: String,
    wall_seconds: u64,
    cpu_seconds: u64,
    pid_limit: u64,
    fd_limit: u64,
}

fn values(cmdline: &str) -> HashMap<&str, &str> {
    cmdline
        .split_whitespace()
        .filter_map(|part| part.split_once('='))
        .collect()
}

fn parse_positive(values: &HashMap<&str, &str>, key: &str) -> Result<u64> {
    let value = values
        .get(key)
        .with_context(|| format!("missing kernel argument {key}"))?
        .parse::<u64>()
        .with_context(|| format!("invalid kernel argument {key}"))?;
    if value == 0 {
        bail!("kernel argument {key} must be positive");
    }
    Ok(value)
}

fn parse_policy(cmdline: &str) -> Result<BuildPolicy> {
    let values = values(cmdline);
    let pool = values
        .get("rune.build_pool")
        .context("missing kernel argument rune.build_pool")?
        .to_string();
    let language = values
        .get("rune.language")
        .context("missing kernel argument rune.language")?
        .to_string();
    let wall_seconds = parse_positive(&values, "rune.wall_seconds")?;
    let cpu_seconds = parse_positive(&values, "rune.cpu_seconds")?;
    let pid_limit = parse_positive(&values, "rune.pid_limit")?;
    let fd_limit = parse_positive(&values, "rune.fd_limit")?;
    Ok(BuildPolicy {
        pool,
        language,
        wall_seconds,
        cpu_seconds,
        pid_limit,
        fd_limit,
    })
}

fn build_command(policy: &BuildPolicy) -> Result<(String, Vec<String>)> {
    let command = match policy.language.as_str() {
        "javascript" => ("rune-build-scriptc", vec!["/input/source.js", "/artifact"]),
        "typescript" => ("rune-build-scriptc", vec!["/input/source.ts", "/artifact"]),
        "rust" => (
            "rustc",
            vec![
                "--edition=2024",
                "-O",
                "/input/source.rs",
                "-o",
                "/artifact",
            ],
        ),
        "c" => ("clang", vec!["-O2", "/input/source.c", "-o", "/artifact"]),
        "cpp" => (
            "clang++",
            vec!["-O2", "/input/source.cpp", "-o", "/artifact"],
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
        "python" => ("rune-build-python", vec!["/input/source.py", "/artifact"]),
        "ruby" => ("rune-build-ruby", vec!["/input/source.rb", "/artifact"]),
        other => bail!("unsupported build language: {other}"),
    };

    Ok((
        command.0,
        command.1.into_iter().map(str::to_owned).collect(),
    ))
}

fn main() -> Result<()> {
    mount_guest_filesystems()?;
    let cmdline =
        fs::read_to_string("/proc/cmdline").context("failed to read kernel command line")?;
    if values(&cmdline).get("rune.cache_warm") == Some(&"scriptc") {
        return warm_scriptc_cache(&cmdline);
    }
    let policy = parse_policy(&cmdline)?;

    for path in ["/work", "/input"] {
        fs::create_dir_all(path)?;
    }
    mount(
        "/dev/vdb",
        "/work",
        "ext4",
        libc::MS_NOSUID | libc::MS_NODEV,
        "",
    )
    .context("failed to mount bounded build scratch disk")?;
    mount(
        "/dev/vdc",
        "/input",
        "ext4",
        libc::MS_RDONLY | libc::MS_NOSUID | libc::MS_NODEV | libc::MS_NOEXEC,
        "",
    )
    .context("failed to mount build input")?;
    if policy.pool == "scriptc" {
        fs::create_dir_all("/cache-seed")?;
        mount(
            "/dev/vdd",
            "/cache-seed",
            "ext4",
            libc::MS_RDONLY | libc::MS_NOSUID | libc::MS_NODEV | libc::MS_NOEXEC,
            "",
        )
        .context("failed to mount ScriptC cache seed")?;
    }
    fs::set_permissions("/work", fs::Permissions::from_mode(0o700))?;
    chown("/work", WORKER_UID, WORKER_GID)?;

    set_limit(libc::RLIMIT_CPU, policy.cpu_seconds)?;
    set_limit(libc::RLIMIT_NPROC, policy.pid_limit)?;
    set_limit(libc::RLIMIT_NOFILE, policy.fd_limit)?;

    drop_privileges()?;
    run_build(&policy)
}

fn warm_scriptc_cache(cmdline: &str) -> Result<()> {
    let values = values(cmdline);
    fs::create_dir_all("/work")?;
    mount(
        "/dev/vdb",
        "/work",
        "ext4",
        libc::MS_NOSUID | libc::MS_NODEV,
        "",
    )
    .context("failed to mount ScriptC cache disk")?;
    fs::set_permissions("/work", fs::Permissions::from_mode(0o700))?;
    chown("/work", WORKER_UID, WORKER_GID)?;
    set_limit(
        libc::RLIMIT_CPU,
        parse_positive(&values, "rune.wall_seconds")?,
    )?;
    set_limit(
        libc::RLIMIT_NPROC,
        parse_positive(&values, "rune.pid_limit")?,
    )?;
    set_limit(
        libc::RLIMIT_NOFILE,
        parse_positive(&values, "rune.fd_limit")?,
    )?;
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
            diagnose_scriptc_cache_warm(profile);
            signal_cache_warm("RUNE_CACHE_WARM_FAILED");
        }
    }
    signal_cache_warm("RUNE_CACHE_WARM_DONE");
}

fn signal_cache_warm(marker: &str) -> ! {
    unsafe { libc::sync() };
    println!("{marker}");
    let _ = std::io::stdout().flush();
    loop {
        thread::park();
    }
}

fn diagnose_scriptc_cache_warm(profile: &str) {
    const DIAGNOSTIC: &str = r#"
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
const require = createRequire('/usr/local/lib/node_modules/scriptc/package.json');
const compiler = await import(pathToFileURL(require.resolve('@scriptc/compiler')).href);
try {
  await compiler.warmNativeCaches({ profiles: [process.argv[1]] });
} catch (error) {
  let current = error;
  while (current) {
    console.error('SCRIPTC_CAUSE:', current.stack ?? current.message ?? String(current));
    current = current.cause;
  }
  process.exitCode = 1;
}
"#;
    let _ = Command::new("node")
        .args(["--input-type=module", "-e", DIAGNOSTIC, profile])
        .env_clear()
        .env("PATH", "/usr/local/bin:/usr/bin:/bin")
        .env("HOME", "/work")
        .env("TMPDIR", "/work")
        .env("SCRIPTC_CACHE_DIR", "/work")
        .current_dir("/work")
        .status();
}

fn run_build(policy: &BuildPolicy) -> Result<()> {
    let (program, args) = build_command(policy)?;
    let mut command = Command::new(program);
    command
        .args(args)
        .env_clear()
        .env("PATH", "/usr/local/bin:/usr/bin:/bin")
        .env("HOME", "/work")
        .env("TMPDIR", "/work/tmp")
        .env("NUGET_PACKAGES", "/opt/rune/nuget")
        .current_dir("/work")
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());
    fs::create_dir_all("/work/tmp")?;

    let mut child = command.spawn().context("failed to start compiler")?;
    let stdout = child.stdout.take().context("missing compiler stdout")?;
    let stderr = child.stderr.take().context("missing compiler stderr")?;
    let stdout_reader = thread::spawn(move || read_bounded(stdout));
    let stderr_reader = thread::spawn(move || read_bounded(stderr));
    let status = child.wait().context("failed to wait for compiler")?;
    let stdout = stdout_reader
        .join()
        .map_err(|_| anyhow::anyhow!("compiler stdout reader panicked"))??;
    let stderr = stderr_reader
        .join()
        .map_err(|_| anyhow::anyhow!("compiler stderr reader panicked"))??;

    if !stdout.is_empty() {
        eprint!("{}", String::from_utf8_lossy(&stdout));
    }
    if !stderr.is_empty() {
        eprint!("{}", String::from_utf8_lossy(&stderr));
    }
    if !status.success() {
        println!("RUNE_BUILD_FAILED");
        bail!("compiler exited with {status}");
    }

    if policy.language == "csharp" {
        let published = fs::read_dir("/work/publish")?
            .filter_map(Result::ok)
            .map(|entry| entry.path())
            .find(|path| path.is_file() && path.extension().is_none())
            .context("Native AOT build did not produce an executable")?;
        fs::copy(published, "/artifact")?;
    }

    let metadata = fs::metadata("/artifact").context("compiler did not produce /artifact")?;
    if metadata.len() == 0 {
        bail!("compiler produced an empty artifact");
    }
    fs::set_permissions("/artifact", fs::Permissions::from_mode(0o755))?;
    unsafe { libc::sync() };
    println!("RUNE_BUILD_DONE");
    Ok(())
}

fn read_bounded(mut reader: impl Read) -> Result<Vec<u8>> {
    let mut bytes = Vec::new();
    reader
        .by_ref()
        .take((MAX_DIAGNOSTIC_BYTES + 1) as u64)
        .read_to_end(&mut bytes)?;
    if bytes.len() > MAX_DIAGNOSTIC_BYTES {
        bytes.truncate(MAX_DIAGNOSTIC_BYTES);
        bytes.extend_from_slice(b"\n[compiler diagnostics truncated]\n");
    }
    Ok(bytes)
}

fn mount_guest_filesystems() -> Result<()> {
    fs::create_dir_all("/proc")?;
    mount(
        "proc",
        "/proc",
        "proc",
        libc::MS_NOSUID | libc::MS_NODEV,
        "",
    )?;
    Ok(())
}

fn drop_privileges() -> Result<()> {
    let setgroups_result = unsafe { libc::setgroups(0, std::ptr::null()) };
    if setgroups_result != 0 {
        return Err(std::io::Error::last_os_error()).context("setgroups failed");
    }
    let gid_result = unsafe { libc::setgid(WORKER_GID) };
    if gid_result != 0 {
        return Err(std::io::Error::last_os_error()).context("setgid failed");
    }
    let uid_result = unsafe { libc::setuid(WORKER_UID) };
    if uid_result != 0 {
        return Err(std::io::Error::last_os_error()).context("setuid failed");
    }
    Ok(())
}

fn chown(path: &str, uid: libc::uid_t, gid: libc::gid_t) -> Result<()> {
    let path = CString::new(path)?;
    let result = unsafe { libc::chown(path.as_ptr(), uid, gid) };
    if result != 0 {
        return Err(std::io::Error::last_os_error()).context("chown failed");
    }
    Ok(())
}

fn mount(source: &str, target: &str, fstype: &str, flags: libc::c_ulong, data: &str) -> Result<()> {
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
        return Err(std::io::Error::last_os_error())
            .with_context(|| format!("mount failed for {target:?}"));
    }
    Ok(())
}

fn set_limit(resource: libc::__rlimit_resource_t, value: u64) -> Result<()> {
    let limit = libc::rlimit {
        rlim_cur: value,
        rlim_max: value,
    };
    let result = unsafe { libc::setrlimit(resource, &limit) };
    if result != 0 {
        return Err(std::io::Error::last_os_error()).context("setrlimit failed");
    }
    Ok(())
}
