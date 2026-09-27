#!/usr/bin/env bash
set -euo pipefail

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

cat >"$tmp/envelope.json" <<'EOF'
{"executionId":"e","invocationId":"i","runeId":"r","runeName":"python-smoke","guildId":1,"eventType":"messageCreate","artifact":{"id":"unused","digest":"unused","entrypoint":"rune","sizeBytes":1},"payload":{},"enqueuedAt":"2026-09-27T00:00:00Z"}
EOF

cat >"$tmp/rune.py" <<'EOF'
import json

print(json.dumps({
    "actions": [],
    "error": None,
}))
EOF

bash firecracker/build-rootfs.sh build python

descriptor="$(bash firecracker/run-build-vm.sh python python "$tmp/rune.py")"
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

echo "python build -> execute OK"

cat >"$tmp/invalid.py" <<'EOF'
if True print("broken")
EOF

if output="$(bash firecracker/run-build-vm.sh python python "$tmp/invalid.py" 2>&1)"; then
  echo "python invalid source unexpectedly built" >&2
  exit 1
fi

grep -Fq "Python compilation failed" <<<"$output"
grep -Fq "rune.py" <<<"$output"
! grep -Eq '/input/|/work/|Kernel panic|console=' <<<"$output"

echo "python diagnostics OK"
