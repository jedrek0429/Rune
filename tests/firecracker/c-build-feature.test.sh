#!/usr/bin/env bash
set -euo pipefail

guest=native/Rune.Firecracker.BuildGuest/src/main.rs
launcher=firecracker/run-build-vm.sh
rootfs=firecracker/build-rootfs.sh

grep -q '"c"' "$guest"
grep -q '"clang"' "$guest"
grep -q 'clang/c)' "$launcher"
grep -q 'clang)' "$rootfs"

echo 'Firecracker C build contract OK'
