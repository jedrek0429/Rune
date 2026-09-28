#!/usr/bin/env bash
set -euo pipefail

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

cat >"$tmp/envelope.json" <<'EOF'
{"executionId":"e","invocationId":"i","runeId":"r","runeName":"cpp-smoke","guildId":1,"eventType":"messageCreate","artifact":{"id":"unused","digest":"unused","entrypoint":"rune","sizeBytes":1},"payload":{},"enqueuedAt":"2026-08-31T00:00:00Z"}
EOF

cat >"$tmp/rune.cpp" <<'EOF'
#include <iostream>
int main() {
    std::cout << "{\"actions\":[],\"error\":null}" << std::endl;
    return 0;
}
EOF

[[ -r "$root/build-images/clang/rootfs.ext4" ]] ||   bash firecracker/build-rootfs.sh build clang

descriptor="$(bash firecracker/run-build-vm.sh clang cpp "$tmp/rune.cpp")"
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
echo "c++ build -> execute OK"

printf 'int main() { return missing; }\n' >"$tmp/invalid.cpp"
if output="$(bash firecracker/run-build-vm.sh clang cpp "$tmp/invalid.cpp" 2>&1)"; then
  echo "c++ invalid source unexpectedly built" >&2
  exit 1
fi
grep -Fq "C++ compilation failed" <<<"$output"
grep -Fq "rune.cpp" <<<"$output"
! grep -Eq '/input/|/work/|Kernel panic|console=' <<<"$output"
echo "c++ diagnostics OK"
