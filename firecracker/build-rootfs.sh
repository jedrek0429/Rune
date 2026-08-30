#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "usage: $0 <invocation|build> <profile>" >&2
  exit 2
fi

kind="$1"
profile="$2"
case "$kind/$profile" in
  invocation/native) base=debian:bookworm-slim; size=256; dir=images/native; arg=RUNTIME ;;
  invocation/python) base=python:3.13-slim-bookworm; size=384; dir=images/python; arg=RUNTIME ;;
  invocation/ruby) base=ruby:3.4-slim-bookworm; size=384; dir=images/ruby; arg=RUNTIME ;;
  build/scriptc) base=node:24-bookworm; size=1536; dir=build-images/scriptc; arg=POOL ;;
  build/clang) base=debian:bookworm-slim; size=768; dir=build-images/clang; arg=POOL ;;
  build/rust) base=rust:1-bookworm; size=1536; dir=build-images/rust; arg=POOL ;;
  build/dotnet-aot) base=mcr.microsoft.com/dotnet/sdk:10.0-bookworm-slim; size=3072; dir=build-images/dotnet-aot; arg=POOL ;;
  build/python) base=python:3.13-slim-bookworm; size=512; dir=build-images/python; arg=POOL ;;
  build/ruby) base=ruby:3.4-slim-bookworm; size=512; dir=build-images/ruby; arg=POOL ;;
  *) echo "unsupported rootfs profile: $kind/$profile" >&2; exit 2 ;;
esac

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
rootfs="$root/$dir/rootfs.ext4"
tag="rune-firecracker-$kind-$profile:dev"
tmp="$(mktemp -d)"
cid=""

cleanup() {
  [[ -z "$cid" ]] || docker rm -f "$cid" >/dev/null 2>&1 || true
  rm -rf "$tmp"
}
trap cleanup EXIT

command -v docker >/dev/null
command -v mkfs.ext4 >/dev/null
mkdir -p "$(dirname "$rootfs")"

docker build \
  --file "firecracker/images/Dockerfile.$kind" \
  --build-arg "BASE_IMAGE=$base" \
  --build-arg "$arg=$profile" \
  --tag "$tag" .

cid="$(docker create "$tag")"
mkdir -p "$tmp/rootfs"
docker export "$cid" | tar -C "$tmp/rootfs" -xf -

rm -f "$rootfs"
truncate -s "${RUNE_ROOTFS_MB:-$size}M" "$rootfs"
mkfs.ext4 -q -F -d "$tmp/rootfs" "$rootfs"
chmod 0444 "$rootfs"
echo "built $rootfs"
