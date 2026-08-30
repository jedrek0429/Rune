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

fn parse_policy(cmdline: &str) -> Result<BuildPolicy> {
    let values: HashMap<_, _> = cmdline
        .split_whitespace()
        .filter_map(|part| part.split_once('='))
        .collect();

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
        ("scriptc", "javascript") => (
            "rune-build-scriptc",
            vec!["/input/source.js", output],
        ),
        ("scriptc", "typescript") => (
            "rune-build-scriptc",
            vec!["/input/source.ts", output],
        ),
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
    fs::set_permissions("/work", fs::Permissions::from_mode(0o700))?;
    chown("/work", WORKER_UID, WORKER_GID)?;

    set_limit(libc::RLIMIT_CPU, policy.cpu_seconds)?;
    set_limit(libc::RLIMIT_NPROC, policy.pid_limit)?;
    set_limit(libc::RLIMIT_NOFILE, policy.fd_limit)?;

    drop_privileges()?;
    run_build(&policy)
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
    fs::write("/work/diagnostics.txt", diagnostics)?;

    if !status.success() {
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
            "root=/dev/vda ro rune.build_pool=dotnet-aot rune.language=csharp rune.pid_limit=128 rune.fd_limit=256 rune.wall_seconds=60",
        )
        .unwrap();

        assert_eq!(
            policy,
            BuildPolicy {
                pool: "dotnet-aot".into(),
                language: "csharp".into(),
                pid_limit: 128,
                fd_limit: 256,
                cpu_seconds: 60,
            }
        );
    }

    #[test]
    fn rejects_unknown_build_pool() {
        let error = parse_policy(
            "rune.build_pool=node rune.language=javascript rune.pid_limit=128 rune.fd_limit=256 rune.wall_seconds=30",
        )
        .unwrap_err()
        .to_string();
        assert!(error.contains("unsupported build pool/language pair"));
    }

    #[test]
    fn rejects_missing_or_zero_limits() {
        assert!(
            parse_policy(
                "rune.build_pool=rust rune.language=rust rune.pid_limit=0 rune.fd_limit=256 rune.wall_seconds=45"
            )
            .is_err()
        );
        assert!(
            parse_policy(
                "rune.build_pool=rust rune.language=rust rune.pid_limit=128 rune.wall_seconds=45"
            )
            .is_err()
        );
    }

    #[test]
    fn diagnostics_are_capped_while_stream_is_fully_drained() {
        let input = vec![b'x'; MAX_DIAGNOSTIC_BYTES * 2];
        let kept = drain_bounded(input.as_slice());
        assert_eq!(kept.len(), MAX_DIAGNOSTIC_BYTES);
    }

    #[test]
    fn compiler_commands_cover_every_language() {
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
        assert!(build_command("scriptc", "python").is_err());
    }

    #[test]
    fn every_direct_compiler_targets_the_executable_artifact() {
        for pair in [
            ("scriptc", "javascript"),
            ("scriptc", "typescript"),
            ("rust", "rust"),
            ("clang", "c"),
            ("clang", "cpp"),
            ("python", "python"),
            ("ruby", "ruby"),
        ] {
            let (_, args) = build_command(pair.0, pair.1).unwrap();
            assert!(args.iter().any(|arg| arg == "/work/artifact"));
        }
    }

    #[test]
    fn read_only_inputs_are_never_compiler_working_directories() {
        let (_, csharp) = build_command("dotnet-aot", "csharp").unwrap();
        assert!(csharp.iter().any(|arg| arg == "/work/project/Rune.csproj"));
        assert!(!csharp.iter().any(|arg| arg == "/input/Rune.csproj"));
    }
}
