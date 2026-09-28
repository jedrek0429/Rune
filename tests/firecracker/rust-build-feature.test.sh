#!/usr/bin/env bash
set -euo pipefail

guest=native/Rune.Firecracker.BuildGuest/src/main.rs
launcher=firecracker/run-build-vm.sh
rootfs=firecracker/build-rootfs.sh

grep -q '"rust"' "$guest"
grep -q 'rustc' "$guest"
grep -q 'rust/rust)' "$launcher"
grep -q 'rust)' "$rootfs"

echo 'Firecracker Rust build contract OK'
