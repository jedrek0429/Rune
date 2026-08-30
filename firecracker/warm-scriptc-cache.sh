#!/usr/bin/env bash
set -euo pipefail

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
firecracker="${RUNE_FIRECRACKER:-firecracker}"
kernel="${RUNE_KERNEL:-$root/vmlinux}"
rootfs="$root/build-images/scriptc/rootfs.ext4"
cache="$root/build-images/scriptc/cache.ext4"
tmp="$(mktemp -d)"
api_sock="$tmp/firecracker.sock"
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

for dependency in curl python3 truncate mkfs.ext4 e2fsck "$firecracker"; do
  command -v "$dependency" >/dev/null 2>&1 || { echo "missing dependency: $dependency" >&2; exit 1; }
done
[[ -r "$kernel" ]] || { echo "missing kernel: $kernel" >&2; exit 1; }
[[ -r "$rootfs" ]] || { echo "missing ScriptC build rootfs: $rootfs" >&2; exit 1; }

rm -f "$cache"
truncate -s 256M "$cache"
mkfs.ext4 -q -F "$cache"

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
  curl --fail --silent --show-error --unix-socket "$api_sock" \
    -X PUT -H 'Content-Type: application/json' -d "$2" \
    "http://localhost$1" >/dev/null
}

api_put /machine-config '{"vcpu_count":2,"mem_size_mib":512,"smt":false}'
api_put /boot-source "{\"kernel_image_path\":$(json_string "$kernel"),\"boot_args\":\"console=ttyS0 reboot=k panic=1 pci=off root=/dev/vda ro init=/sbin/rune-build-guest rune.cache_warm=scriptc rune.pid_limit=128 rune.fd_limit=512 rune.wall_seconds=120\"}"
api_put /drives/rootfs "{\"drive_id\":\"rootfs\",\"path_on_host\":$(json_string "$rootfs"),\"is_root_device\":true,\"is_read_only\":true}"
api_put /drives/cache "{\"drive_id\":\"cache\",\"path_on_host\":$(json_string "$cache"),\"is_root_device\":false,\"is_read_only\":false}"
api_put /entropy '{}'
# Deliberately no network interface.
api_put /actions '{"action_type":"InstanceStart"}'

set +e
timeout 120s bash -c '
  log="$1"; pid="$2"
  while kill -0 "$pid" 2>/dev/null; do
    grep -q "RUNE_CACHE_WARM_DONE" "$log" && exit 0
    grep -q "RUNE_CACHE_WARM_FAILED" "$log" && exit 3
    sleep 0.05
  done
  exit 4
' _ "$console_log" "$pid"
status=$?
set -e
if [[ "$status" -ne 0 ]]; then
  cat "$console_log" >&2 || true
  [[ "$status" -eq 124 ]] && echo "ScriptC cache warm exceeded 120s" >&2
  exit "$status"
fi

kill "$pid" >/dev/null 2>&1 || true
wait "$pid" 2>/dev/null || true
pid=""

set +e
e2fsck -p "$cache"
fsck_status=$?
set -e
if (( fsck_status > 1 )); then
  echo "ScriptC cache filesystem check failed with status $fsck_status" >&2
  exit "$fsck_status"
fi

chmod 0444 "$cache"
echo "warmed ScriptC cache seed at $cache"