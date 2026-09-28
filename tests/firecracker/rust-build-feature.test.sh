#!/usr/bin/env bash
set -euo pipefail

guest=src/Rune.Firecracker.BuildGuest/src/main.rs
launcher=src/Rune.Firecracker/run-build-vm.sh
rootfs=src/Rune.Firecracker/build-rootfs.sh
image=src/Rune.Firecracker/images/Dockerfile.build
helper=src/Rune.Firecracker/build-tools/rust.sh

grep -q '"rust"' "$guest"
grep -q 'rune-build-rust' "$guest"
grep -q 'rust/rust)' "$launcher"
grep -q 'rust)' "$rootfs"
grep -q 'rune-build-rust' "$image"
grep -q 'serde_json' "$image"
grep -q 'cargo build' "$helper"

echo 'Firecracker Rust build contract OK'
