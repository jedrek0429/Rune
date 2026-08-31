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

if [[ $# -ne 3 ]]; then
  echo "usage: $0 <scriptc|clang|rust|dotnet-aot|python|ruby> <language> <source>" >&2
  exit 2
fi

pool="$1"
language="$2"
source_path="$3"
profile="$(rune_build_profile "$pool")" || {
  echo "unsupported build pool: $pool" >&2
  exit 2
}
read -r vcpu mem_mib disk_mib wall_seconds pid_limit fd_limit <<<"$profile"
[[ -f "$source_path" ]] || { echo "source file is missing" >&2; exit 2; }
(( $(wc -c <"$source_path") <= 65536 )) || { echo "Rune source exceeds 64 KiB" >&2; exit 2; }

case "$language" in
  javascript) input_name=source.js ;;
  typescript) input_name=source.ts ;;
  rust) input_name=source.rs ;;
  c) input_name=source.c ;;
  cpp) input_name=source.cpp ;;
  csharp) input_name=Program.cs ;;
  python) input_name=source.py ;;
  ruby) input_name=source.rb ;;
  *) echo "unsupported Rune language: $language" >&2; exit 2 ;;
esac

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
firecracker="${RUNE_FIRECRACKER:-firecracker}"
kernel="${RUNE_KERNEL:-$root/vmlinux}"
rootfs="$root/build-images/$pool/rootfs.ext4"
cache_seed="$root/build-images/scriptc/cache.ext4"
artifacts="$root/artifacts"
tmp="$(mktemp -d)"
api_sock="$tmp/firecracker.sock"
vsock_sock="$tmp/vsock.sock"
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

for dependency in curl python3 truncate mkfs.ext4 e2fsck debugfs sha256sum "$firecracker"; do
  command -v "$dependency" >/dev/null 2>&1 || {
    echo "missing dependency: $dependency" >&2
    exit 1
  }
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
mkfs.ext4 -q -F -d "$input_dir" "$input"

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
  local endpoint="$1"
  local response="$tmp/api-response"
  local status
  status="$(curl --silent --show-error --output "$response" --write-out '%{http_code}' \
    --unix-socket "$api_sock" -X PUT -H 'Content-Type: application/json' -d "$2" \
    "http://localhost$endpoint")" || {
      echo "Firecracker API request failed: PUT $endpoint" >&2
      cat "$response" >&2 2>/dev/null || true
      return 1
    }
  if [[ "$status" -lt 200 || "$status" -ge 300 ]]; then
    echo "Firecracker API request failed: PUT $endpoint returned HTTP $status" >&2
    cat "$response" >&2 2>/dev/null || true
    echo >&2
    return 1
  fi
}

stop_vm_and_finalize_scratch() {
  if [[ -n "$pid" ]]; then
    kill "$pid" >/dev/null 2>&1 || true
    wait "$pid" 2>/dev/null || true
    pid=""
  fi

  set +e
  e2fsck -p "$scratch" >/dev/null
  local fsck_status=$?
  set -e
  if (( fsck_status > 1 )); then
    echo "build scratch filesystem check failed with status $fsck_status" >&2
    return "$fsck_status"
  fi
}

show_guest_diagnostics() {
  debugfs -R "dump -p /diagnostics.txt $diagnostics" "$scratch" >/dev/null 2>&1 || true
  [[ ! -s "$diagnostics" ]] || cat "$diagnostics" >&2
}

api_put /machine-config "{\"vcpu_count\":$vcpu,\"mem_size_mib\":$mem_mib,\"smt\":false}"
api_put /boot-source "{\"kernel_image_path\":$(json_string "$kernel"),\"boot_args\":\"console=ttyS0 reboot=k panic=1 pci=off root=/dev/vda ro init=/sbin/rune-build-guest rune.build_pool=$pool rune.language=$language rune.pid_limit=$pid_limit rune.fd_limit=$fd_limit rune.wall_seconds=$wall_seconds\"}"
api_put /drives/rootfs "{\"drive_id\":\"rootfs\",\"path_on_host\":$(json_string "$rootfs"),\"is_root_device\":true,\"is_read_only\":true}"
api_put /drives/scratch "{\"drive_id\":\"scratch\",\"path_on_host\":$(json_string "$scratch"),\"is_root_device\":false,\"is_read_only\":false}"
api_put /drives/input "{\"drive_id\":\"input\",\"path_on_host\":$(json_string "$input"),\"is_root_device\":false,\"is_read_only\":true}"
if [[ "$pool" == scriptc ]]; then
  api_put /drives/cache_seed "{\"drive_id\":\"cache_seed\",\"path_on_host\":$(json_string "$cache_seed"),\"is_root_device\":false,\"is_read_only\":true}"
fi
api_put /entropy '{}'
api_put /vsock "{\"guest_cid\":3,\"uds_path\":$(json_string "$vsock_sock")}"
# Deliberately no network interface.
api_put /actions '{"action_type":"InstanceStart"}'

set +e
timeout "${wall_seconds}s" bash -c '
  log="$1"; pid="$2"
  while kill -0 "$pid" 2>/dev/null; do
    grep -q "RUNE_BUILD_DONE" "$log" && exit 0
    grep -q "RUNE_BUILD_FAILED" "$log" && exit 3
    sleep 0.05
  done
  exit 4
' _ "$console_log" "$pid"
status=$?
set -e

stop_vm_and_finalize_scratch

if [[ "$status" -ne 0 ]]; then
  show_guest_diagnostics
  cat "$console_log" >&2 || true
  [[ "$status" -eq 124 ]] && echo "Rune build exceeded ${wall_seconds}s wall-time limit" >&2
  [[ "$status" -eq 4 ]] && echo "build VM exited before reporting completion" >&2
  exit "$status"
fi

if ! debugfs -R "dump -p /artifact $artifact" "$scratch" >/dev/null 2>&1 || [[ ! -f "$artifact" ]]; then
  show_guest_diagnostics
  cat "$console_log" >&2 || true
  echo "build VM reported completion but produced no artifact" >&2
  exit 1
fi
size="$(wc -c <"$artifact")"
(( size > 0 && size <= 16777216 )) || { echo "built artifact exceeds 16 MiB" >&2; exit 1; }
digest="$(sha256sum "$artifact" | cut -d' ' -f1)"
mkdir -p "$artifacts"
target="$artifacts/$digest"
if [[ ! -e "$target" ]]; then
  install -m 0444 "$artifact" "$target"
fi
printf 'sha256:%s %s rune\n' "$digest" "$size"