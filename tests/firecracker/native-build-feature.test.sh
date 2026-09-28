#!/usr/bin/env bash
set -euo pipefail

for file in \
  native/Rune.Firecracker.BuildGuest/Cargo.toml \
  native/Rune.Firecracker.BuildGuest/src/main.rs \
  firecracker/images/Dockerfile.build \
  firecracker/build-rootfs.sh \
  firecracker/run-build-vm.sh; do
  test -f "$file"
done

guest=native/Rune.Firecracker.BuildGuest/src/main.rs
launcher=firecracker/run-build-vm.sh
rootfs=firecracker/build-rootfs.sh

grep -q '"rust"' "$guest"
grep -q '"c"' "$guest"
grep -q '"cpp"' "$guest"
grep -q 'rustc' "$guest"
grep -q 'clang' "$guest"
grep -q 'clang++' "$guest"
grep -q 'setgroups' "$guest"
grep -q 'setuid' "$guest"
grep -q 'setgid' "$guest"

grep -q '64 \* 1024' "$launcher"
grep -q '16 \* 1024 \* 1024' "$launcher"
grep -q 'rust)' "$rootfs"
grep -q 'clang)' "$rootfs"

if grep -q '/network-interfaces' "$launcher"; then
  echo 'build VMs must not configure networking' >&2
  exit 1
fi

echo 'Firecracker native build contract OK'
