#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: $0 <javascript|python|rust>" >&2
  exit 2
fi

language="$1"
case "$language" in
  javascript|python) default_mem_mib=256 ;;
  rust) default_mem_mib=768 ;;
  *) echo "unsupported language: $language" >&2; exit 2 ;;
esac

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
firecracker="${RUNE_FIRECRACKER:-firecracker}"
kernel="${RUNE_KERNEL:-$root/vmlinux}"
rootfs="$root/images/$language/rootfs.ext4"
snapshot_dir="$root/snapshots/$language"
mem_mib="${RUNE_VM_MEMORY_MIB:-$default_mem_mib}"
tmp="$(mktemp -d)"
api_sock="$tmp/firecracker.sock"
vsock_sock="$tmp/vsock.sock"
console_log="$tmp/console.log"
pid=""

cleanup() {
  if [[ -n "$pid" ]]; then kill "$pid" >/dev/null 2>&1 || true; wait "$pid" 2>/dev/null || true; fi
  rm -rf "$tmp"
}
trap cleanup EXIT

for dependency in curl python3 "$firecracker"; do
  command -v "$dependency" >/dev/null 2>&1 || { echo "missing dependency: $dependency" >&2; exit 1; }
done
[[ -r "$kernel" ]] || { echo "missing kernel: $kernel" >&2; exit 1; }
[[ -r "$rootfs" ]] || { echo "missing rootfs: $rootfs" >&2; exit 1; }

mkdir -p "$snapshot_dir"
rm -f "$snapshot_dir/vmstate" "$snapshot_dir/memory"

"$firecracker" --api-sock "$api_sock" >"$console_log" 2>&1 &
pid=$!

for _ in $(seq 1 400); do
  [[ -S "$api_sock" ]] && break
  kill -0 "$pid" 2>/dev/null || { cat "$console_log" >&2; exit 1; }
  sleep 0.01
done
[[ -S "$api_sock" ]] || { echo "Firecracker API socket did not appear" >&2; exit 1; }

json_string() {
  python3 -c 'import json,sys; print(json.dumps(sys.argv[1]))' "$1"
}

api_put() {
  local path="$1"
  local data="$2"
  curl --fail --silent --show-error \
    --unix-socket "$api_sock" \
    -X PUT \
    -H 'Content-Type: application/json' \
    -d "$data" \
    "http://localhost$path" >/dev/null
}

api_patch() {
  local path="$1"
  local data="$2"
  curl --fail --silent --show-error \
    --unix-socket "$api_sock" \
    -X PATCH \
    -H 'Content-Type: application/json' \
    -d "$data" \
    "http://localhost$path" >/dev/null
}

api_put /machine-config "{\"vcpu_count\":1,\"mem_size_mib\":$mem_mib,\"smt\":false}"
api_put /boot-source "{\"kernel_image_path\":$(json_string "$kernel"),\"boot_args\":\"console=ttyS0 reboot=k panic=1 pci=off root=/dev/vda ro init=/sbin/rune-guest\"}"
api_put /drives/rootfs "{\"drive_id\":\"rootfs\",\"path_on_host\":$(json_string "$rootfs"),\"is_root_device\":true,\"is_read_only\":true}"
api_put /vsock "{\"guest_cid\":3,\"uds_path\":$(json_string "$vsock_sock")}"
api_put /actions '{"action_type":"InstanceStart"}'

ready=0
for _ in $(seq 1 1200); do
  if grep -q "RUNE_READY $language" "$console_log"; then
    ready=1
    break
  fi
  kill -0 "$pid" 2>/dev/null || { cat "$console_log" >&2; exit 1; }
  sleep 0.05
done

if [[ "$ready" -ne 1 ]]; then
  echo "guest did not become ready" >&2
  cat "$console_log" >&2
  exit 1
fi

api_patch /vm '{"state":"Paused"}'
api_put /snapshot/create "{\"snapshot_type\":\"Full\",\"snapshot_path\":$(json_string "$snapshot_dir/vmstate"),\"mem_file_path\":$(json_string "$snapshot_dir/memory")}"

chmod 0444 "$snapshot_dir/vmstate" "$snapshot_dir/memory"
echo "built warm $language snapshot in $snapshot_dir"
