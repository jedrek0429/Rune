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
    language: String,
    pid_limit: u64,
    fd_limit: u64,
    cpu_seconds: u64,
}

fn values(cmdline: &str) -> HashMap<&str, &str> {
    cmdline
        .split_whitespace()
        .filter_map(|part| part.split_once('='))
        .collect()
}

fn parse_policy(cmdline: &str) -> Result<BuildPolicy> {
    let values = values(cmdline);
    let pool = values
        .get("rune.build_pool")
        .context("missing rune.build_pool")?
        .to_string();
    let language = values
        .get("rune.language")
        .context("missing rune.language")?
        .to_string();
    build_command(&pool, &language)?;

    Ok(BuildPolicy {
        pool,
        language,
        pid_limit: parse_positive(&values, "rune.pid_limit")?,
        fd_limit: parse_positive(&values, "rune.fd_limit")?,
        cpu_seconds: parse_positive(&values, "rune.wall_seconds")?,
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

fn build_command(pool: &str, language: &str) -> Result<(&'static str, Vec<String>)> {
    let output = "/work/artifact";
    let command = match (pool, language) {
        ("scriptc", "javascript") => ("rune-build-scriptc", vec!["/input/source.js", output]),
        ("scriptc", "typescript") => ("rune-build-scriptc", vec!["/input/source.ts", output]),
        ("rust", "rust") => ("rustc", vec!["/input/source.rs", "-O", "-o", output]),
        ("clang", "c") => ("clang", vec!["/input/source.c", "-O2", "-o", output]),
        ("clang", "cpp") => ("clang++", vec!["/input/source.cpp", "-O2", "-o", output]),
        ("dotnet-aot", "csharp") => (
            "dotnet",
            vec![
                "publish",
                "/work/project/Rune.csproj",
                "-c",
                "Release",
                "-o",
                "/work/publish",
                "-p:PublishAot=true",
                "-p:RestoreIgnoreFailedSources=true",
            ],
        ),
        ("python", "python") => ("rune-build-python", vec!["/input/source.py", output]),
        ("ruby", "ruby") => ("rune-build-ruby", vec!["/input/source.rb", output]),
        _ => bail!("unsupported build pool/language pair {pool}/{language}"),
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
            unsafe { libc::sync() };
            println!("RUNE_CACHE_WARM_FAILED");
            bail!("ScriptC {profile} cache warm exited with {status}");
        }
    }
    unsafe { libc::sync() };
    println!("RUNE_CACHE_WARM_DONE");
    Ok(())
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
    fs::create_dir_all("/work/tmp")?;
    if policy.language == "csharp" {
        fs::create_dir_all("/work/project")?;
        fs::copy("/input/Program.cs", "/work/project/Program.cs")?;
        fs::copy("/input/Rune.csproj", "/work/project/Rune.csproj")?;
    }

    let (program, args) = build_command(&policy.pool, &policy.language)?;
    let mut child = Command::new(program)
        .args(args)
        .env_clear()
        .env("PATH", "/usr/local/bin:/usr/bin:/bin")
        .env("HOME", "/work")
        .env("TMPDIR", "/work/tmp")
        .env("NUGET_PACKAGES", "/opt/rune/nuget")
        .current_dir("/work")
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .with_context(|| format!("failed to start {program}"))?;

    let stdout = child.stdout.take().context("build stdout was not piped")?;
    let stderr = child.stderr.take().context("build stderr was not piped")?;
    let stdout_thread = thread::spawn(move || drain_bounded(stdout));
    let stderr_thread = thread::spawn(move || drain_bounded(stderr));

    let status = child.wait().context("failed waiting for compiler")?;
    let mut diagnostics = stdout_thread.join().unwrap_or_default();
    diagnostics.extend(stderr_thread.join().unwrap_or_default());
    diagnostics.truncate(MAX_DIAGNOSTIC_BYTES);
    fs::write("/work/diagnostics.txt", &diagnostics)?;

    if !status.success() {
        unsafe { libc::sync() };
        if !diagnostics.is_empty() {
            eprintln!("{}", String::from_utf8_lossy(&diagnostics));
        }
        println!("RUNE_BUILD_FAILED");
        bail!("compiler exited with {status}");
    }

    if policy.language == "csharp" {
        fs::copy("/work/publish/Rune", "/work/artifact")?;
    }

    let metadata = fs::metadata("/work/artifact").context("compiler produced no artifact")?;
    if metadata.len() == 0 {
        bail!("compiler produced an empty artifact");
    }

    unsafe { libc::sync() };
    println!("RUNE_BUILD_DONE");
    Ok(())
}

fn drain_bounded(mut reader: impl Read) -> Vec<u8> {
    let mut kept = Vec::with_capacity(MAX_DIAGNOSTIC_BYTES);
    let mut buffer = [0_u8; 8192];
    while let Ok(read) = reader.read(&mut buffer) {
        if read == 0 {
            break;
        }
        let remaining = MAX_DIAGNOSTIC_BYTES.saturating_sub(kept.len());
        kept.extend_from_slice(&buffer[..read.min(remaining)]);
    }
    kept
}

fn mount_guest_filesystems() -> Result<()> {
    fs::create_dir_all("/proc")?;
    mount(
        "proc",
        "/proc",
        "proc",
        libc::MS_NOSUID | libc::MS_NODEV | libc::MS_NOEXEC,
        "",
    )?;
    Ok(())
}

fn mount(
    source: &str,
    target: &str,
    fs_type: &str,
    flags: libc::c_ulong,
    data: &str,
) -> Result<()> {
    let source = CString::new(source)?;
    let target = CString::new(target)?;
    let fs_type = CString::new(fs_type)?;
    let data = CString::new(data)?;
    let result = unsafe {
        libc::mount(
            source.as_ptr(),
            target.as_ptr(),
            fs_type.as_ptr(),
            flags,
            data.as_ptr().cast(),
        )
    };
    if result != 0 {
        bail!(
            "mount {source:?} on {target:?} failed: {}",
            std::io::Error::last_os_error()
        );
    }
    Ok(())
}

fn chown(path: &str, uid: libc::uid_t, gid: libc::gid_t) -> Result<()> {
    let path = CString::new(path)?;
    let result = unsafe { libc::chown(path.as_ptr(), uid, gid) };
    if result != 0 {
        bail!("chown failed: {}", std::io::Error::last_os_error());
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
        bail!("setrlimit failed: {}", std::io::Error::last_os_error());
    }
    Ok(())
}

fn drop_privileges() -> Result<()> {
    if unsafe { libc::setgroups(0, std::ptr::null()) } != 0 {
        bail!("setgroups failed: {}", std::io::Error::last_os_error());
    }
    if unsafe { libc::setgid(WORKER_GID) } != 0 {
        bail!("setgid failed: {}", std::io::Error::last_os_error());
    }
    if unsafe { libc::setuid(WORKER_UID) } != 0 {
        bail!("setuid failed: {}", std::io::Error::last_os_error());
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn build_commands_cover_all_language_pools() {
        assert_eq!(
            build_command("scriptc", "javascript").unwrap().0,
            "rune-build-scriptc"
        );
        assert_eq!(
            build_command("scriptc", "typescript").unwrap().0,
            "rune-build-scriptc"
        );
        assert_eq!(build_command("rust", "rust").unwrap().0, "rustc");
        assert_eq!(build_command("clang", "c").unwrap().0, "clang");
        assert_eq!(build_command("clang", "cpp").unwrap().0, "clang++");
        assert_eq!(build_command("dotnet-aot", "csharp").unwrap().0, "dotnet");
        assert_eq!(
            build_command("python", "python").unwrap().0,
            "rune-build-python"
        );
        assert_eq!(build_command("ruby", "ruby").unwrap().0, "rune-build-ruby");
    }

    #[test]
    fn invalid_pool_language_pair_is_rejected() {
        assert!(build_command("scriptc", "python").is_err());
    }

    #[test]
    fn policy_requires_positive_limits() {
        assert!(parse_policy(
            "rune.build_pool=clang rune.language=c rune.pid_limit=0 rune.fd_limit=1 rune.wall_seconds=1"
        )
        .is_err());
    }
}
