#!/usr/bin/env bash
set -euo pipefail

for language in javascript python rust; do
  ./firecracker/build-rootfs.sh "$language"
  ./firecracker/build-snapshot.sh "$language"
done
