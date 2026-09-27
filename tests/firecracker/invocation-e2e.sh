#!/usr/bin/env bash
set -euo pipefail

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
tmp="$(mktemp -d)"
cleanup() { rm -rf "$tmp"; }
trap cleanup EXIT

for dependency in cc docker mkfs.ext4 firecracker; do
  command -v "$dependency" >/dev/null 2>&1 || { echo "missing dependency: $dependency" >&2; exit 1; }
done
[[ -e /dev/kvm ]] || { echo '/dev/kvm is unavailable' >&2; exit 1; }
[[ -r "$root/vmlinux" ]] || { echo "missing kernel: $root/vmlinux" >&2; exit 1; }

cat >"$tmp/rune.c" <<'C'
#include <stdio.h>

int main(void) {
    char buffer[4096];
    while (fread(buffer, 1, sizeof buffer, stdin) != 0) {}
    fputs("{\"actions\":[],\"error\":null,\"marker\":\"firecracker-e2e\"}\n", stdout);
    return 0;
}
C
cc -O2 "$tmp/rune.c" -o "$tmp/rune"

cat >"$tmp/envelope.json" <<'JSON'
{"executionId":"00000000-0000-0000-0000-000000000001","invocationId":"00000000-0000-0000-0000-000000000002","runeId":"00000000-0000-0000-0000-000000000003","runeName":"e2e","guildId":1,"eventType":"messageCreate","artifact":{"id":"sha256:e2e","digest":"sha256:e2e","entrypoint":"rune","sizeBytes":1},"payload":{},"enqueuedAt":"2026-08-31T00:00:00Z"}
JSON

bash firecracker/build-invocation-rootfs.sh
response="$(bash firecracker/run-invocation-vm.sh "$tmp/rune" "$tmp/envelope.json")"
grep -q '"marker":"firecracker-e2e"' <<<"$response"
grep -q '"error":null' <<<"$response"

echo 'Firecracker invocation e2e OK'
