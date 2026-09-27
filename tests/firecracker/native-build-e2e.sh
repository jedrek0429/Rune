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
    println!("{}", r#"{"actions":[],"error":null}"#);
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

assert_invalid_build() {
  local pool="$1" language="$2" source="$3" title="$4" filename="$5"
  local output
  if output="$(bash firecracker/run-build-vm.sh "$pool" "$language" "$source" 2>&1)"; then
    echo "$language invalid source unexpectedly built" >&2
    exit 1
  fi
  grep -Fq "$title compilation failed" <<<"$output"
  grep -Fq "$filename" <<<"$output"
  ! grep -Eq '/input/|/work/|Kernel panic|console=' <<<"$output"
}

build_and_execute rust rust "$tmp/rune.rs"
build_and_execute clang c "$tmp/rune.c"
build_and_execute clang cpp "$tmp/rune.cpp"

printf 'fn main() { let value = ; }\n' >"$tmp/invalid.rs"
printf 'int main(void) { return missing; }\n' >"$tmp/invalid.c"
printf 'int main() { return missing; }\n' >"$tmp/invalid.cpp"

assert_invalid_build rust rust "$tmp/invalid.rs" Rust rune.rs
assert_invalid_build clang c "$tmp/invalid.c" C rune.c
assert_invalid_build clang cpp "$tmp/invalid.cpp" "C++" rune.cpp

cat generated/rust/rune_api.rs >"$tmp/generated-rune.rs"
cat >>"$tmp/generated-rune.rs" <<'EOF'

struct FakeHost;

impl RuneHost for FakeHost {
    fn message_reply(
        &mut self,
        reply_message: &ReplyMessageProperties,
    ) -> Result<RestMessage, String> {
        Ok(RestMessage {
            id: 99,
            channel_id: 2,
            content: reply_message
                .content
                .clone()
                .unwrap_or_default(),
            author: User {
                id: 3,
                username: "rune".to_string(),
            },
        })
    }
}

fn main() {
    let message = Message {
        id: 1,
        channel_id: 2,
        content: "hello".to_string(),
        author: User {
            id: 3,
            username: "rune".to_string(),
        },
    };

    let mut host = FakeHost;
    let reply = message.reply(
        &mut host,
        ReplyMessageProperties {
            content: Some("generated".to_string()),
        },
    ).unwrap();

    assert_eq!(reply.content, "generated");
    assert_eq!(
        REST_MESSAGE_REPLY_NETCORD,
        "NetCord.Rest.RestMessage.ReplyAsync",
    );

    println!("{}", r#"{"actions":[],"error":null}"#);
}
EOF

build_and_execute rust rust "$tmp/generated-rune.rs"
echo "generated rust Rune.Api wrapper -> execute OK"
