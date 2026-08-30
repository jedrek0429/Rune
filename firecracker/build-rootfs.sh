#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "usage: $0 <invocation|build> <profile>" >&2
  exit 2
fi

kind="$1"
profile="$2"
case "$kind/$profile" in
  invocation/rune) base=debian:bookworm-slim; size=256; dir=images/rune ;;
  build/scriptc) base=node:24-bookworm; size=1536; dir=build-images/scriptc ;;
  build/clang) base=debian:bookworm-slim; size=768; dir=build-images/clang ;;
  build/rust) base=rust:1-bookworm; size=1536; dir=build-images/rust ;;
  build/dotnet-aot) base=mcr.microsoft.com/dotnet/sdk:10.0-bookworm-slim; size=3072; dir=build-images/dotnet-aot ;;
  build/python) base=debian:bookworm-slim; size=1024; dir=build-images/python ;;
  build/ruby) base=debian:bookworm-slim; size=768; dir=build-images/ruby ;;
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

args=(
  --file "firecracker/images/Dockerfile.$kind"
  --build-arg "BASE_IMAGE=$base"
)
[[ "$kind" == build ]] && args+=(--build-arg "POOL=$profile")
docker build "${args[@]}" --tag "$tag" .

cid="$(docker create "$tag")"
mkdir -p "$tmp/rootfs"
docker export "$cid" | tar -C "$tmp/rootfs" -xf -

used_mib="$(du -sm "$tmp/rootfs" | cut -f1)"
needed_mib=$((used_mib + used_mib / 4 + 64))
if (( needed_mib > size )); then
  size="$needed_mib"
fi
size="${RUNE_ROOTFS_MB:-$size}"

rm -f "$rootfs"
truncate -s "${size}M" "$rootfs"
mkfs.ext4 -q -F -d "$tmp/rootfs" "$rootfs"
chmod 0444 "$rootfs"

if [[ "$kind/$profile" == build/scriptc ]]; then
  bash firecracker/warm-scriptc-cache.sh
fi

echo "built $rootfs (${size} MiB)"
