#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=/dev/null
source "$script_dir/resource-policy.sh"

if [[ "${1:-}" == "--print-profile" ]]; then
  [[ $# -eq 2 ]] || exit 2
  rune_build_profile "$2"
  exit $?
fi

if [[ $# -ne 1 ]]; then
  echo "usage: $0 <scriptc|clang|rust|dotnet-aot|python|ruby>" >&2
  exit 2
fi

pool="$1"
read -r vcpu mem_mib disk_mib wall_seconds pid_limit fd_limit \
  <<<"$(rune_build_profile "$pool")" || {
    echo "unsupported build pool: $pool" >&2
    exit 2
  }

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
firecracker="${RUNE_FIRECRACKER:-firecracker}"
kernel="${RUNE_KERNEL:-$root/vmlinux}"
rootfs="$root/build-images/$pool/rootfs.ext4"
result_dir="$root/build-results"
tmp="$(mktemp -d)"
api_sock="$tmp/firecracker.sock"
vsock_sock="$tmp/vsock.sock"
console_log="$tmp/console.log"
scratch="$tmp/scratch.ext4"
scratch_disk_mib="$disk_mib"
pid=""

cleanup() {
  if [[ -n "$pid" ]]; then
    kill "$pid" >/dev/null 2>&1 || true
    wait "$pid" 2>/dev/null || true
  fi
  rm -rf "$tmp"
}
trap cleanup EXIT

for dependency in curl python3 truncate mkfs.ext4 timeout "$firecracker"; do
  command -v "$dependency" >/dev/null 2>&1 || {
    echo "missing dependency: $dependency" >&2
    exit 1
  }
done
[[ -r "$kernel" ]] || { echo "missing kernel: $kernel" >&2; exit 1; }
[[ -r "$rootfs" ]] || { echo "missing build rootfs: $rootfs" >&2; exit 1; }

truncate -s "${scratch_disk_mib}M" "$scratch"
mkfs.ext4 -q -F "$scratch"

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

api_put /machine-config "{\"vcpu_count\":$vcpu,\"mem_size_mib\":$mem_mib,\"smt\":false}"
api_put /boot-source "{\"kernel_image_path\":$(json_string "$kernel"),\"boot_args\":\"console=ttyS0 reboot=k panic=1 pci=off root=/dev/vda ro init=/sbin/rune-build-guest rune.build_pool=$pool rune.pid_limit=$pid_limit rune.fd_limit=$fd_limit rune.wall_seconds=$wall_seconds\"}"
api_put /drives/rootfs "{\"drive_id\":\"rootfs\",\"path_on_host\":$(json_string "$rootfs"),\"is_root_device\":true,\"is_read_only\":true}"
api_put /drives/scratch "{\"drive_id\":\"scratch\",\"path_on_host\":$(json_string "$scratch"),\"is_root_device\":false,\"is_read_only\":false}"
api_put /vsock "{\"guest_cid\":3,\"uds_path\":$(json_string "$vsock_sock")}"
# No NIC is configured for compiler VMs. Toolchains and dependencies must be pre-baked.
api_put /actions '{"action_type":"InstanceStart"}'

set +e
timeout "${wall_seconds}s" bash -c '
  log="$1"
  pid="$2"
  while kill -0 "$pid" 2>/dev/null; do
    if grep -q "RUNE_BUILD_DONE" "$log"; then exit 0; fi
    if grep -q "RUNE_BUILD_FAILED" "$log"; then exit 3; fi
    sleep 0.05
  done
  exit 4
' _ "$console_log" "$pid"
status=$?
set -e

if [[ "$status" -ne 0 ]]; then
  cat "$console_log" >&2 || true
  if [[ "$status" -eq 124 ]]; then
    echo "Rune build exceeded ${wall_seconds}s wall-time limit" >&2
  else
    echo "Rune build VM failed with status $status" >&2
  fi
  exit "$status"
fi

mkdir -p "$result_dir"
result="$result_dir/${pool}-$(date +%s)-$$.ext4"
mv "$scratch" "$result"
chmod 0444 "$result"
echo "$result"
