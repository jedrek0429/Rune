#!/usr/bin/env bash
set -euo pipefail

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
rootfs="$root/images/rune/rootfs.ext4"
tmp="$(mktemp -d)"
cid=""

cleanup() {
  [[ -z "$cid" ]] || docker rm -f "$cid" >/dev/null 2>&1 || true
  rm -rf "$tmp"
}
trap cleanup EXIT

for dependency in docker mkfs.ext4; do
  command -v "$dependency" >/dev/null 2>&1 || { echo "missing dependency: $dependency" >&2; exit 1; }
done

mkdir -p "$(dirname "$rootfs")"
docker build --file firecracker/images/Dockerfile.invocation --tag rune-firecracker-invocation:dev .
cid="$(docker create rune-firecracker-invocation:dev)"
mkdir -p "$tmp/rootfs"
docker export "$cid" | tar -C "$tmp/rootfs" -xf -

used_mib="$(du -sm "$tmp/rootfs" | cut -f1)"
size=$((used_mib + used_mib / 4 + 64))
(( size < 256 )) && size=256
rm -f "$rootfs"
truncate -s "${size}M" "$rootfs"
mkfs.ext4 -q -F -d "$tmp/rootfs" "$rootfs"
chmod 0444 "$rootfs"
echo "built $rootfs (${size} MiB)"
