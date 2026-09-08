#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 || "$1" != build ]]; then
  echo "usage: $0 build <scriptc|rust|clang|dotnet-aot|python|ruby>" >&2
  exit 2
fi

profile="$2"
case "$profile" in
  scriptc) base=ubuntu:24.04; size=1536 ;;
  rust) base=rust:1-bookworm; size=1536 ;;
  clang) base=debian:bookworm-slim; size=768 ;;
  dotnet-aot) base=debian:bookworm-slim; size=3072 ;;
  python) base=debian:bookworm-slim; size=1024 ;;
  ruby) base=debian:bookworm-slim; size=768 ;;
  *) echo "unsupported build profile: $profile" >&2; exit 2 ;;
esac

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
rootfs="$root/build-images/$profile/rootfs.ext4"
tag="rune-firecracker-build-$profile:dev"
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
  --file firecracker/images/Dockerfile.build \
  --build-arg "BASE_IMAGE=$base" \
  --build-arg "POOL=$profile" \
  --tag "$tag" .
cid="$(docker create "$tag")"
mkdir -p "$tmp/rootfs"
docker export "$cid" | tar -C "$tmp/rootfs" -xf -

used_mib="$(du -sm "$tmp/rootfs" | cut -f1)"
needed_mib=$((used_mib + used_mib / 4 + 64))
(( needed_mib > size )) && size="$needed_mib"
rm -f "$rootfs"
truncate -s "${size}M" "$rootfs"
mkfs.ext4 -q -F -d "$tmp/rootfs" "$rootfs"
chmod 0444 "$rootfs"

if [[ "$profile" == scriptc ]]; then
  bash firecracker/warm-scriptc-cache.sh
fi

echo "built $rootfs (${size} MiB)"
