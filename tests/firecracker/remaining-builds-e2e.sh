#!/usr/bin/env bash
set -euo pipefail

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

cat >"$tmp/envelope.json" <<'EOF'
{"executionId":"e","invocationId":"i","runeId":"r","runeName":"language-smoke","guildId":1,"eventType":"messageCreate","artifact":{"id":"unused","digest":"unused","entrypoint":"rune","sizeBytes":1},"payload":{},"enqueuedAt":"2026-09-08T00:00:00Z"}
EOF

cat >"$tmp/rune.js" <<'EOF'
console.log(JSON.stringify({ actions: [], error: null }));
EOF

cat >"$tmp/rune.ts" <<'EOF'
console.log(JSON.stringify({ actions: [], error: null }));
EOF

cat >"$tmp/rune.py" <<'EOF'
print('{"actions":[],"error":null}')
EOF

cat >"$tmp/rune.rb" <<'EOF'
puts '{"actions":[],"error":null}'
EOF

cat >"$tmp/Rune.cs" <<'EOF'
using System;
Console.WriteLine("{\"actions\":[],\"error\":null}");
EOF

for profile in scriptc python ruby dotnet-aot; do
  bash firecracker/build-rootfs.sh build "$profile"
done

build_and_execute() {
  local pool="$1" language="$2" source="$3"
  local descriptor id digest artifact response
  descriptor="$(bash firecracker/run-build-vm.sh "$pool" "$language" "$source")"
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
  echo "$language build -> execute OK"
}

build_and_execute scriptc javascript "$tmp/rune.js"
build_and_execute scriptc typescript "$tmp/rune.ts"
build_and_execute python python "$tmp/rune.py"
build_and_execute ruby ruby "$tmp/rune.rb"
build_and_execute dotnet-aot csharp "$tmp/Rune.cs"

echo 'Remaining Rune language build -> execute smoke OK'
