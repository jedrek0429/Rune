#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: $0 <javascript|python|rust>" >&2
  exit 2
fi

language="$1"
case "$language" in
  javascript|python) default_size_mb=384 ;;
  rust) default_size_mb=1536 ;;
  *) echo "unsupported language: $language" >&2; exit 2 ;;
esac

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
size_mb="${RUNE_ROOTFS_MB:-$default_size_mb}"
image_dir="$root/images/$language"
rootfs="$image_dir/rootfs.ext4"
tag="rune-firecracker-$language:dev"
tmp="$(mktemp -d)"
cid=""

cleanup() {
  if [[ -n "$cid" ]]; then docker rm -f "$cid" >/dev/null 2>&1 || true; fi
  rm -rf "$tmp"
}
trap cleanup EXIT

command -v docker >/dev/null
command -v mkfs.ext4 >/dev/null

mkdir -p "$image_dir"

docker build \
  --file "firecracker/images/Dockerfile.$language" \
  --tag "$tag" \
  .

cid="$(docker create "$tag")"
mkdir -p "$tmp/rootfs"
docker export "$cid" | tar -C "$tmp/rootfs" -xf -

rm -f "$rootfs"
truncate -s "${size_mb}M" "$rootfs"
mkfs.ext4 -q -F -d "$tmp/rootfs" "$rootfs"
chmod 0444 "$rootfs"

echo "built $rootfs"
