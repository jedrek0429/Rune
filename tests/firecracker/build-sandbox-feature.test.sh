#!/usr/bin/env bash
set -euo pipefail

for file in   src/Rune.Firecracker.BuildGuest/Cargo.toml   src/Rune.Firecracker.BuildGuest/src/main.rs   src/Rune.Firecracker/images/Dockerfile.build   src/Rune.Firecracker/build-rootfs.sh   src/Rune.Firecracker/run-build-vm.sh   src/Rune.Firecracker/generated-runtime.sh; do
  test -f "$file"
done

guest=src/Rune.Firecracker.BuildGuest/src/main.rs
launcher=src/Rune.Firecracker/run-build-vm.sh

grep -q 'setgroups' "$guest"
grep -q 'setuid' "$guest"
grep -q 'setgid' "$guest"
grep -q '64 \* 1024' "$launcher"
grep -q 'generated-runtime.sh' "$launcher"
grep -q 'RUNE_MAX_ARTIFACT_BYTES' "$launcher"

if grep -q '/network-interfaces' "$launcher"; then
  echo 'build VMs must not configure networking' >&2
  exit 1
fi

echo 'Firecracker build sandbox contract OK'
