#!/usr/bin/env bash
set -euo pipefail

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

cat >"$tmp/envelope.json" <<'EOF'
{"executionId":"e","invocationId":"i","runeId":"r","runeName":"typescript-smoke","guildId":1,"eventType":"messageCreate","artifact":{"id":"unused","digest":"unused","entrypoint":"rune","sizeBytes":1},"payload":{},"enqueuedAt":"2026-09-27T00:00:00Z"}
EOF

cat >"$tmp/rune.ts" <<'EOF'
const result: { actions: string[]; error: string | null } = {
    actions: [],
    error: null,
};
console.log(JSON.stringify(result));
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

cat >"$tmp/invalid.ts" <<'EOF'
const value: number = ;
EOF

if output="$(bash firecracker/run-build-vm.sh scriptc typescript "$tmp/invalid.ts" 2>&1)"; then
  echo "typescript invalid source unexpectedly built" >&2
  exit 1
fi

grep -Fq "TypeScript compilation failed" <<<"$output"
grep -Fq "rune.ts" <<<"$output"
! grep -Eq '/input/|/work/|Kernel panic|console=' <<<"$output"

echo "typescript diagnostics OK"

cat generated/typescript/rune-api.ts >"$tmp/generated-rune.ts"
cat >>"$tmp/generated-rune.ts" <<'EOF'

const generatedMessage = new Message({
    id: "1",
    channelId: "2",
    content: "hello",
    author: {
        id: "3",
        username: "rune",
    },
});

if (generatedMessage.channelId !== "2") {
    throw new Error("generated Message did not hydrate channelId");
}

if (
    REST_MESSAGE_REPLY.netCord !==
    "NetCord.Rest.RestMessage.ReplyAsync"
) {
    throw new Error("generated reply identity is wrong");
}

console.log(JSON.stringify({ actions: [], error: null }));
EOF

descriptor="$(bash firecracker/run-build-vm.sh scriptc typescript "$tmp/generated-rune.ts")"
read -r id _ _ <<<"$descriptor"
[[ "$id" == sha256:* ]]
digest="${id#sha256:}"
artifact="$root/artifacts/$digest"
test -s "$artifact"
response="$(bash firecracker/run-invocation-vm.sh "$artifact" "$tmp/envelope.json")"

python3 - "$response" <<'PY'
import json
import sys

result = json.loads(sys.argv[1])
assert result == {"actions": [], "error": None}, result
PY

echo "generated typescript Rune.Api wrapper -> execute OK"
