#!/usr/bin/env bash
set -euo pipefail

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

cat >"$tmp/envelope.json" <<'EOF'
{"executionId":"e","invocationId":"i","runeId":"r","runeName":"rust-smoke","guildId":1,"eventType":"messageCreate","artifact":{"id":"unused","digest":"unused","entrypoint":"rune","sizeBytes":1},"payload":{},"enqueuedAt":"2026-08-31T00:00:00Z"}
EOF

cat >"$tmp/rune.rs" <<'EOF'
fn main() {
    println!("{}", r#"{"actions":[],"error":null}"#);
}
EOF

[[ -r "$root/build-images/rust/rootfs.ext4" ]] ||   bash src/Rune.Firecracker/build-rootfs.sh build rust

build_and_execute() {
  local source="$1"
  local descriptor id digest artifact response
  descriptor="$(bash src/Rune.Firecracker/run-build-vm.sh rust rust "$source")"
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
}

build_and_execute "$tmp/rune.rs"
echo "rust build -> execute OK"

printf 'fn main() { let value = ; }\n' >"$tmp/invalid.rs"
if output="$(bash src/Rune.Firecracker/run-build-vm.sh rust rust "$tmp/invalid.rs" 2>&1)"; then
  echo "rust invalid source unexpectedly built" >&2
  exit 1
fi
grep -Fq "Rust compilation failed" <<<"$output"
grep -Fq "rune.rs" <<<"$output"
! grep -Eq '/input/|/work/|Kernel panic|console=' <<<"$output"
echo "rust diagnostics OK"

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

build_and_execute "$tmp/generated-rune.rs"
echo "generated rust Rune.Api wrapper -> execute OK"
