#!/usr/bin/env bash
set -euo pipefail

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

cat >"$tmp/envelope.json" <<'EOF'
{"executionId":"e","invocationId":"i","runeId":"r","runeName":"native-smoke","guildId":1,"eventType":"messageCreate","artifact":{"id":"unused","digest":"unused","entrypoint":"rune","sizeBytes":1},"payload":{},"enqueuedAt":"2026-08-31T00:00:00Z"}
EOF

cat >"$tmp/rune.rs" <<'EOF'
fn main() {
    println!(r#"{"actions":[],"error":null}"#);
}
EOF

cat >"$tmp/rune.c" <<'EOF'
#include <stdio.h>
int main(void) {
    puts("{\"actions\":[],\"error\":null}");
    return 0;
}
EOF

cat >"$tmp/rune.cpp" <<'EOF'
#include <iostream>
int main() {
    std::cout << "{\"actions\":[],\"error\":null}" << std::endl;
    return 0;
}
EOF

bash firecracker/build-rootfs.sh build rust
bash firecracker/build-rootfs.sh build clang

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

build_and_execute rust rust "$tmp/rune.rs"
build_and_execute clang c "$tmp/rune.c"
build_and_execute clang cpp "$tmp/rune.cpp"
