#!/usr/bin/env bash
set -euo pipefail

for path in \
  src/Rune.Firecracker.Runner/Cargo.toml \
  src/Rune.Firecracker.Runner/src/main.rs \
  src/Rune.Firecracker.Runner/src/protocol.rs \
  src/Rune.Firecracker.Runner/src/generated_protocol.rs \
  src/Rune.Firecracker.Runner/src/queue.rs \
  src/Rune.Firecracker.Runner/src/pool.rs \
  src/Rune.Firecracker.Runner/src/firecracker.rs \
  src/Rune.Firecracker/build-snapshot.sh; do
  [[ -f "$path" ]] || { echo "missing $path" >&2; exit 1; }
done

grep -q 'rune:invocations' src/Rune.Firecracker.Runner/src/generated_protocol.rs
grep -q 'rune:results' src/Rune.Firecracker.Runner/src/generated_protocol.rs
grep -q 'rune-runners' src/Rune.Firecracker.Runner/src/generated_protocol.rs
grep -q 'target_for_backlog' src/Rune.Firecracker.Runner/src/pool.rs
grep -q 'WarmVm::restore' src/Rune.Firecracker.Runner/src/pool.rs
grep -q 'vm.destroy().await' src/Rune.Firecracker.Runner/src/main.rs
grep -q 'sha256:' src/Rune.Firecracker.Runner/src/protocol.rs
grep -q 'resume_vm' src/Rune.Firecracker.Runner/src/firecracker.rs
grep -q 'snapshot_type.*Full' src/Rune.Firecracker/build-snapshot.sh

echo 'Firecracker runner and warm-pool contract OK'
