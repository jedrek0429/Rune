#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=/dev/null
source "$script_dir/resource-policy.sh"

if [[ "${1:-}" == "--print-profile" ]]; then
  [[ $# -eq 2 ]] || exit 2
  read -r vcpu mem_mib _ <<<"$(rune_invocation_profile "$2")" || exit 2
  echo "$vcpu $mem_mib"
  exit 0
fi

if [[ $# -ne 1 ]]; then
  echo "usage: $0 <native|python|ruby>" >&2
  exit 2
fi

runtime="$1"
read -r vcpu default_mem_mib _tmp_mib _cpu_seconds _pid_limit _fd_limit \
  <<<"$(rune_invocation_profile "$runtime")" || {
    echo "unsupported invocation runtime: $runtime" >&2
    exit 2
  }

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
firecracker="${RUNE_FIRECRACKER:-firecracker}"
kernel="${RUNE_KERNEL:-$root/vmlinux}"
rootfs="$root/images/$runtime/rootfs.ext4"
snapshot_dir="$root/snapshots/$runtime"
mem_mib="${RUNE_VM_MEMORY_MIB:-$default_mem_mib}"
tmp="$(mktemp -d)"
api_sock="$tmp/firecracker.sock"
vsock_sock="$tmp/vsock.sock"
console_log="$tmp/console.log"
pid=""

cleanup() {
  if [[ -n "$pid" ]]; then
    kill "$pid" >/dev/null 2>&1 || true
    wait "$pid" 2>/dev/null || true
  fi
  rm -rf "$tmp"
}
trap cleanup EXIT

for dependency in curl python3 "$firecracker"; do
  command -v "$dependency" >/dev/null 2>&1 || {
    echo "missing dependency: $dependency" >&2
    exit 1
  }
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

api_put /machine-config "{\"vcpu_count\":$vcpu,\"mem_size_mib\":$mem_mib,\"smt\":false}"
api_put /boot-source "{\"kernel_image_path\":$(json_string "$kernel"),\"boot_args\":\"console=ttyS0 reboot=k panic=1 pci=off root=/dev/vda ro init=/sbin/rune-guest\"}"
api_put /drives/rootfs "{\"drive_id\":\"rootfs\",\"path_on_host\":$(json_string "$rootfs"),\"is_root_device\":true,\"is_read_only\":true}"
api_put /vsock "{\"guest_cid\":3,\"uds_path\":$(json_string "$vsock_sock")}"
# No NIC is configured for Rune invocation VMs.
api_put /actions '{"action_type":"InstanceStart"}'

ready=0
for _ in $(seq 1 1200); do
  if grep -q "RUNE_READY $runtime" "$console_log"; then
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
echo "built warm $runtime snapshot in $snapshot_dir"
