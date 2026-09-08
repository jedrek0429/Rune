#!/usr/bin/env bash
set -euo pipefail

MAX_SOURCE_BYTES=$((64 * 1024))
MAX_ARTIFACT_BYTES=$((16 * 1024 * 1024))

if [[ $# -ne 3 ]]; then
  echo "usage: $0 <scriptc|rust|clang|dotnet-aot|python|ruby> <language> <source>" >&2
  exit 2
fi

pool="$1"
language="$2"
source_path="$3"
case "$pool/$language" in
  scriptc/javascript) vcpu=1; mem_mib=512; disk_mib=512; wall_seconds=30; pid_limit=128; fd_limit=512; input_name=source.js ;;
  scriptc/typescript) vcpu=1; mem_mib=512; disk_mib=512; wall_seconds=30; pid_limit=128; fd_limit=512; input_name=source.ts ;;
  rust/rust) vcpu=2; mem_mib=1024; disk_mib=512; wall_seconds=45; pid_limit=128; fd_limit=256; input_name=source.rs ;;
  clang/c) vcpu=1; mem_mib=512; disk_mib=512; wall_seconds=20; pid_limit=128; fd_limit=256; input_name=source.c ;;
  clang/cpp) vcpu=1; mem_mib=512; disk_mib=512; wall_seconds=20; pid_limit=128; fd_limit=256; input_name=source.cpp ;;
  dotnet-aot/csharp) vcpu=2; mem_mib=2048; disk_mib=768; wall_seconds=60; pid_limit=128; fd_limit=256; input_name=Program.cs ;;
  python/python) vcpu=1; mem_mib=512; disk_mib=256; wall_seconds=20; pid_limit=128; fd_limit=256; input_name=source.py ;;
  ruby/ruby) vcpu=1; mem_mib=512; disk_mib=256; wall_seconds=20; pid_limit=128; fd_limit=256; input_name=source.rb ;;
  *) echo "unsupported build target: $pool/$language" >&2; exit 2 ;;
esac

[[ -f "$source_path" ]] || { echo "source file is missing" >&2; exit 2; }
(( $(wc -c <"$source_path") <= MAX_SOURCE_BYTES )) || { echo "Rune source exceeds 64 KiB" >&2; exit 2; }

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
firecracker="${RUNE_FIRECRACKER:-firecracker}"
kernel="${RUNE_KERNEL:-$root/vmlinux}"
rootfs="$root/build-images/$pool/rootfs.ext4"
cache_seed="$root/build-images/scriptc/cache.ext4"
artifacts="$root/artifacts"
tmp="$(mktemp -d)"
api_sock="$tmp/firecracker.sock"
console_log="$tmp/console.log"
scratch="$tmp/scratch.ext4"
input="$tmp/input.ext4"
input_dir="$tmp/input"
artifact="$tmp/artifact"
diagnostics="$tmp/diagnostics.txt"
pid=""

cleanup() {
  if [[ -n "$pid" ]]; then
    kill "$pid" >/dev/null 2>&1 || true
    wait "$pid" 2>/dev/null || true
  fi
  rm -rf "$tmp"
}
trap cleanup EXIT

for dependency in curl python3 truncate mkfs.ext4 e2fsck debugfs sha256sum timeout "$firecracker"; do
  command -v "$dependency" >/dev/null 2>&1 || { echo "missing dependency: $dependency" >&2; exit 1; }
done
[[ -r "$kernel" ]] || { echo "missing kernel: $kernel" >&2; exit 1; }
[[ -r "$rootfs" ]] || { echo "missing build rootfs: $rootfs" >&2; exit 1; }
if [[ "$pool" == scriptc && ! -r "$cache_seed" ]]; then
  echo "missing ScriptC cache seed: $cache_seed" >&2
  exit 1
fi

mkdir -p "$input_dir"
cp "$source_path" "$input_dir/$input_name"
if [[ "$language" == csharp ]]; then
  cat >"$input_dir/Rune.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <AssemblyName>Rune</AssemblyName>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
</Project>
EOF
fi

truncate -s "${disk_mib}M" "$scratch"
mkfs.ext4 -q -F "$scratch"
truncate -s 4M "$input"
mkfs.ext4 -q -F -O ^has_journal -d "$input_dir" "$input"

"$firecracker" --api-sock "$api_sock" >"$console_log" 2>&1 &
pid=$!
for _ in $(seq 1 400); do
  [[ -S "$api_sock" ]] && break
  kill -0 "$pid" 2>/dev/null || { cat "$console_log" >&2; exit 1; }
  sleep 0.01
done
[[ -S "$api_sock" ]] || { echo "Firecracker API socket did not appear" >&2; exit 1; }

json_string() { python3 -c 'import json,sys; print(json.dumps(sys.argv[1]))' "$1"; }
api_put() {
  local endpoint="$1" response="$tmp/api-response" status
  status="$(curl --silent --show-error --output "$response" --write-out '%{http_code}' --unix-socket "$api_sock" -X PUT -H 'Content-Type: application/json' -d "$2" "http://localhost$endpoint")"
  if [[ "$status" -lt 200 || "$status" -ge 300 ]]; then
    echo "Firecracker API PUT $endpoint returned HTTP $status" >&2
    cat "$response" >&2 || true
    return 1
  fi
}

api_put /machine-config "{\"vcpu_count\":$vcpu,\"mem_size_mib\":$mem_mib,\"smt\":false}"
api_put /boot-source "{\"kernel_image_path\":$(json_string "$kernel"),\"boot_args\":\"console=ttyS0 reboot=k panic=1 pci=off root=/dev/vda ro init=/sbin/rune-build-guest rune.language=$language rune.cpu_seconds=$wall_seconds rune.pid_limit=$pid_limit rune.fd_limit=$fd_limit\"}"
api_put /drives/rootfs "{\"drive_id\":\"rootfs\",\"path_on_host\":$(json_string "$rootfs"),\"is_root_device\":true,\"is_read_only\":true}"
api_put /drives/scratch "{\"drive_id\":\"scratch\",\"path_on_host\":$(json_string "$scratch"),\"is_root_device\":false,\"is_read_only\":false}"
api_put /drives/input "{\"drive_id\":\"input\",\"path_on_host\":$(json_string "$input"),\"is_root_device\":false,\"is_read_only\":true}"
if [[ "$pool" == scriptc ]]; then
  api_put /drives/cache_seed "{\"drive_id\":\"cache_seed\",\"path_on_host\":$(json_string "$cache_seed"),\"is_root_device\":false,\"is_read_only\":true}"
fi
# Deliberately no network interface.
api_put /actions '{"action_type":"InstanceStart"}'

set +e
timeout "${wall_seconds}s" bash -c '
  while kill -0 "$2" 2>/dev/null; do
    grep -q "RUNE_BUILD_DONE" "$1" && exit 0
    grep -q "RUNE_BUILD_FAILED" "$1" && exit 3
    sleep 0.05
  done
  exit 4
' _ "$console_log" "$pid"
status=$?
set -e
kill "$pid" >/dev/null 2>&1 || true
wait "$pid" 2>/dev/null || true
pid=""
set +e
e2fsck -p "$scratch" >/dev/null
fsck_status=$?
set -e
(( fsck_status <= 1 )) || { echo "build scratch filesystem check failed" >&2; exit "$fsck_status"; }

if [[ "$status" -ne 0 ]]; then
  debugfs -R "dump -p /diagnostics.txt $diagnostics" "$scratch" >/dev/null 2>&1 || true
  [[ ! -s "$diagnostics" ]] || cat "$diagnostics" >&2
  cat "$console_log" >&2 || true
  [[ "$status" -eq 124 ]] && echo "Rune build exceeded ${wall_seconds}s wall-time limit" >&2
  [[ "$status" -eq 4 ]] && echo "build VM exited before reporting completion" >&2
  exit "$status"
fi

debugfs -R "dump -p /artifact $artifact" "$scratch" >/dev/null 2>&1 || { echo "build VM produced no artifact" >&2; exit 1; }
size="$(wc -c <"$artifact")"
(( size > 0 && size <= MAX_ARTIFACT_BYTES )) || { echo "built artifact exceeds 16 MiB" >&2; exit 1; }
digest="$(sha256sum "$artifact" | cut -d' ' -f1)"
mkdir -p "$artifacts"
target="$artifacts/$digest"
[[ -e "$target" ]] || install -m 0444 "$artifact" "$target"
printf 'sha256:%s %s rune\n' "$digest" "$size"
