use anyhow::Result;

#[derive(Debug, Clone, PartialEq, Eq)]
struct BuildPolicy {
    pool: String,
    pid_limit: u64,
    fd_limit: u64,
    cpu_seconds: u64,
}

fn parse_policy(_cmdline: &str) -> Result<BuildPolicy> {
    todo!("implemented after policy tests")
}

fn main() -> Result<()> {
    let cmdline = std::fs::read_to_string("/proc/cmdline")?;
    let _policy = parse_policy(&cmdline)?;
    todo!("implemented after policy tests")
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
        assert!(parse_policy("rune.build_pool=rust rune.pid_limit=0 rune.fd_limit=256 rune.wall_seconds=45").is_err());
        assert!(parse_policy("rune.build_pool=rust rune.pid_limit=128 rune.wall_seconds=45").is_err());
    }
}
