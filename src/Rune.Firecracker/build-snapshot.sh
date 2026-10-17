#!/usr/bin/env bash
set -euo pipefail

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
firecracker="${RUNE_FIRECRACKER:-firecracker}"
kernel="${RUNE_KERNEL:-$root/vmlinux}"
rootfs="$root/images/rune/rootfs.ext4"
snapshot_dir="$root/snapshot"
tmp="$(mktemp -d)"
api_sock="$tmp/firecracker.sock"
vsock_sock="$tmp/vsock.sock"
console_log="$tmp/console.log"
pid=""
cleanup(){ [[ -z "$pid" ]] || { kill "$pid" >/dev/null 2>&1 || true; wait "$pid" 2>/dev/null || true; }; rm -rf "$tmp"; }
trap cleanup EXIT
for d in curl python3 "$firecracker"; do command -v "$d" >/dev/null; done
[[ -r "$kernel" && -r "$rootfs" ]] || { echo 'missing kernel/rootfs' >&2; exit 1; }
mkdir -p "$snapshot_dir"; rm -f "$snapshot_dir/vmstate" "$snapshot_dir/memory"
"$firecracker" --api-sock "$api_sock" >"$console_log" 2>&1 & pid=$!
for _ in $(seq 1 400); do [[ -S "$api_sock" ]] && break; sleep .01; done
json(){ python3 -c 'import json,sys;print(json.dumps(sys.argv[1]))' "$1"; }
put(){ curl --fail --silent --show-error --unix-socket "$api_sock" -X PUT -H 'Content-Type: application/json' -d "$2" "http://localhost$1" >/dev/null; }
patch(){ curl --fail --silent --show-error --unix-socket "$api_sock" -X PATCH -H 'Content-Type: application/json' -d "$2" "http://localhost$1" >/dev/null; }
put /machine-config '{"vcpu_count":1,"mem_size_mib":192,"smt":false}'
put /boot-source "{\"kernel_image_path\":$(json "$kernel"),\"boot_args\":\"console=ttyS0 reboot=k panic=1 pci=off root=/dev/vda ro init=/sbin/rune-guest\"}"
put /drives/rootfs "{\"drive_id\":\"rootfs\",\"path_on_host\":$(json "$rootfs"),\"is_root_device\":true,\"is_read_only\":true}"
put /vsock "{\"guest_cid\":3,\"uds_path\":$(json "$vsock_sock")}"
# Deliberately no network interface.
put /actions '{"action_type":"InstanceStart"}'
for _ in $(seq 1 1200); do grep -q '^RUNE_READY' "$console_log" && break; sleep .05; done
grep -q '^RUNE_READY' "$console_log" || { cat "$console_log" >&2; exit 1; }
patch /vm '{"state":"Paused"}'
put /snapshot/create "{\"snapshot_type\":\"Full\",\"snapshot_path\":$(json "$snapshot_dir/vmstate"),\"mem_file_path\":$(json "$snapshot_dir/memory")}"
chmod 0444 "$snapshot_dir/vmstate" "$snapshot_dir/memory"
echo "built warm Rune snapshot in $snapshot_dir"
