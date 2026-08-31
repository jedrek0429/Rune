#!/usr/bin/env bash
set -euo pipefail

for path in \
  native/Rune.Firecracker.Guest/Cargo.toml \
  native/Rune.Firecracker.Guest/src/main.rs \
  firecracker/images/Dockerfile.invocation \
  firecracker/build-invocation-rootfs.sh \
  firecracker/run-invocation-vm.sh; do
  [[ -f "$path" ]] || { echo "missing $path" >&2; exit 1; }
done

grep -q 'MAX_ARTIFACT_BYTES: usize = 16 \* 1024 \* 1024' native/Rune.Firecracker.Guest/src/main.rs
grep -q 'WRITABLE_TMPFS_MIB: usize = 32' native/Rune.Firecracker.Guest/src/main.rs
grep -q 'setgroups(0' native/Rune.Firecracker.Guest/src/main.rs
grep -q 'setuid(WORKER_UID)' native/Rune.Firecracker.Guest/src/main.rs
grep -q 'AF_VSOCK' native/Rune.Firecracker.Guest/src/main.rs
grep -q 'Deliberately no network interface' firecracker/run-invocation-vm.sh
grep -q 'mem_size_mib.*192' firecracker/run-invocation-vm.sh

echo 'Firecracker invocation contract OK'
