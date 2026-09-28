#!/usr/bin/env bash
set -euo pipefail

guest=src/Rune.Firecracker.BuildGuest/src/main.rs
launcher=src/Rune.Firecracker/run-build-vm.sh
rootfs=src/Rune.Firecracker/build-rootfs.sh

grep -q '"rust"' "$guest"
grep -q 'rustc' "$guest"
grep -q 'rust/rust)' "$launcher"
grep -q 'rust)' "$rootfs"

echo 'Firecracker Rust build contract OK'
