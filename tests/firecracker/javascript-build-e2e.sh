#!/usr/bin/env bash
set -euo pipefail

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

cat >"$tmp/envelope.json" <<'EOF'
{"executionId":"e","invocationId":"i","runeId":"r","runeName":"javascript-smoke","guildId":1,"eventType":"messageCreate","artifact":{"id":"unused","digest":"unused","entrypoint":"rune","sizeBytes":1},"payload":{},"enqueuedAt":"2026-09-27T00:00:00Z"}
EOF

cat >"$tmp/rune.js" <<'EOF'
console.log(JSON.stringify({ actions: [], error: null }));
EOF

bash src/Rune.Firecracker/build-rootfs.sh build scriptc

descriptor="$(bash src/Rune.Firecracker/run-build-vm.sh scriptc javascript "$tmp/rune.js")"
read -r id _ _ <<<"$descriptor"

[[ "$id" == sha256:* ]]

digest="${id#sha256:}"
artifact="$root/artifacts/$digest"
test -s "$artifact"

response="$(bash src/Rune.Firecracker/run-invocation-vm.sh "$artifact" "$tmp/envelope.json")"

python3 - "$response" <<'PY'
import json, sys

result = json.loads(sys.argv[1])
assert result == {"actions": [], "error": None}, result
PY

echo "javascript build -> execute OK"

cat >"$tmp/invalid.js" <<'EOF'
const value = ;
EOF

if output="$(bash src/Rune.Firecracker/run-build-vm.sh scriptc javascript "$tmp/invalid.js" 2>&1)"; then
  echo "javascript invalid source unexpectedly built" >&2
  exit 1
fi

grep -Fq "JavaScript compilation failed" <<<"$output"
grep -Fq "rune.js" <<<"$output"
! grep -Eq '/input/|/work/|Kernel panic|console=' <<<"$output"

echo "javascript diagnostics OK"
