#!/usr/bin/env bash
set -euo pipefail

for path in \
  native/Rune.Firecracker.Runner/Cargo.toml \
  native/Rune.Firecracker.Runner/src/main.rs \
  native/Rune.Firecracker.Runner/src/protocol.rs \
  native/Rune.Firecracker.Runner/src/queue.rs \
  native/Rune.Firecracker.Runner/src/pool.rs \
  native/Rune.Firecracker.Runner/src/firecracker.rs \
  firecracker/build-snapshot.sh; do
  [[ -f "$path" ]] || { echo "missing $path" >&2; exit 1; }
done

grep -q 'rune:invocations' native/Rune.Firecracker.Runner/src/main.rs
grep -q 'rune:results' native/Rune.Firecracker.Runner/src/main.rs
grep -q 'rune-runners' native/Rune.Firecracker.Runner/src/main.rs
grep -q 'target_for_backlog' native/Rune.Firecracker.Runner/src/pool.rs
grep -q 'WarmVm::restore' native/Rune.Firecracker.Runner/src/pool.rs
grep -q 'vm.destroy().await' native/Rune.Firecracker.Runner/src/main.rs
grep -q 'sha256:' native/Rune.Firecracker.Runner/src/protocol.rs
grep -q 'resume_vm' native/Rune.Firecracker.Runner/src/firecracker.rs
grep -q 'snapshot_type.*Full' firecracker/build-snapshot.sh

echo 'Firecracker runner and warm-pool contract OK'
