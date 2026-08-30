#!/usr/bin/env bash
set -euo pipefail

for runtime in native python ruby; do
  ./firecracker/build-rootfs.sh invocation "$runtime"
  ./firecracker/build-snapshot.sh "$runtime"
done

for pool in scriptc clang rust dotnet-aot python ruby; do
  ./firecracker/build-rootfs.sh build "$pool"
done
