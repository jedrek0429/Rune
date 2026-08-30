#!/usr/bin/env bash
set -euo pipefail

./firecracker/build-rootfs.sh invocation rune
./firecracker/build-snapshot.sh

for pool in scriptc clang rust dotnet-aot python ruby; do
  ./firecracker/build-rootfs.sh build "$pool"
done
