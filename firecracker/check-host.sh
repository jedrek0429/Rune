#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != "Linux" ]]; then
  echo "FAIL: Firecracker runner requires Linux." >&2
  exit 1
fi

if [[ ! -c /dev/kvm || ! -r /dev/kvm || ! -w /dev/kvm ]]; then
  echo "FAIL: /dev/kvm must exist and be readable/writable by the runner user." >&2
  exit 1
fi

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
kernel="${RUNE_KERNEL:-$root/vmlinux}"

for command in "${RUNE_FIRECRACKER:-firecracker}" docker mkfs.ext4 curl python3; do
  command -v "$command" >/dev/null 2>&1 || {
    echo "FAIL: missing command: $command" >&2
    exit 1
  }
done

[[ -r "$kernel" ]] || {
  echo "FAIL: Firecracker-compatible uncompressed kernel missing at $kernel" >&2
  exit 1
}

echo "OK: Linux/KVM Firecracker host is ready."
