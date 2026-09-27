#!/usr/bin/env bash
set -euo pipefail

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

cat >"$tmp/envelope.json" <<'EOF'
{"executionId":"e","invocationId":"i","runeId":"r","runeName":"typescript-smoke","guildId":1,"eventType":"messageCreate","artifact":{"id":"unused","digest":"unused","entrypoint":"rune","sizeBytes":1},"payload":{},"enqueuedAt":"2026-09-27T00:00:00Z"}
EOF

cat >"$tmp/rune.ts" <<'EOF'
const result: { actions: unknown[]; error: null } = {
    actions: [],
    error: null,
};
console.log(JSON.stringify({ actions: [], error: null }));
EOF

bash firecracker/build-rootfs.sh build scriptc

descriptor="$(bash firecracker/run-build-vm.sh scriptc typescript "$tmp/rune.ts")"
read -r id _ _ <<<"$descriptor"

[[ "$id" == sha256:* ]]

digest="${id#sha256:}"
artifact="$root/artifacts/$digest"
test -s "$artifact"

response="$(bash firecracker/run-invocation-vm.sh "$artifact" "$tmp/envelope.json")"

python3 - "$response" <<'PY'
import json, sys

result = json.loads(sys.argv[1])
assert result == {"actions": [], "error": None}, result
PY

echo "typescript build -> execute OK"

