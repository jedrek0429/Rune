#!/usr/bin/env bash
set -euo pipefail

for path in \
  src/Rune.Firecracker.Guest/Cargo.toml \
  src/Rune.Firecracker.Guest/src/main.rs \
  src/Rune.Firecracker.Guest/src/generated_runtime.rs \
  src/Rune.Firecracker/generated-runtime.sh \
  src/Rune.Firecracker/images/Dockerfile.invocation \
  src/Rune.Firecracker/build-invocation-rootfs.sh \
  src/Rune.Firecracker/run-invocation-vm.sh; do
  [[ -f "$path" ]] || { echo "missing $path" >&2; exit 1; }
done

grep -q 'MAX_ARTIFACT_BYTES: usize = 16777216' src/Rune.Firecracker.Guest/src/generated_runtime.rs
grep -q 'RUNE_MAX_ARTIFACT_BYTES=16777216' src/Rune.Firecracker/generated-runtime.sh
grep -q 'generated-runtime.sh' src/Rune.Firecracker/run-invocation-vm.sh
grep -q 'WRITABLE_TMPFS_MIB: usize = 32' src/Rune.Firecracker.Guest/src/main.rs
grep -q 'setgroups(0' src/Rune.Firecracker.Guest/src/main.rs
grep -q 'setuid(WORKER_UID)' src/Rune.Firecracker.Guest/src/main.rs
grep -q 'AF_VSOCK' src/Rune.Firecracker.Guest/src/main.rs
grep -q 'Deliberately no network interface' src/Rune.Firecracker/run-invocation-vm.sh
grep -q 'mem_size_mib.*192' src/Rune.Firecracker/run-invocation-vm.sh

echo 'Firecracker invocation contract OK'
