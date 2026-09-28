#!/usr/bin/env bash
set -euo pipefail

guest=src/Rune.Firecracker.BuildGuest/src/main.rs
launcher=src/Rune.Firecracker/run-build-vm.sh
rootfs=src/Rune.Firecracker/build-rootfs.sh

grep -q '"cpp"' "$guest"
grep -q '"clang++"' "$guest"
grep -q 'clang/cpp)' "$launcher"
grep -q 'clang)' "$rootfs"

echo 'Firecracker C++ build contract OK'
