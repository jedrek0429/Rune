#!/usr/bin/env bash
set -euo pipefail

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

cat >"$tmp/envelope.json" <<'EOF'
{"executionId":"e","invocationId":"i","runeId":"r","runeName":"c-smoke","guildId":1,"eventType":"messageCreate","artifact":{"id":"unused","digest":"unused","entrypoint":"rune","sizeBytes":1},"payload":{},"enqueuedAt":"2026-08-31T00:00:00Z"}
EOF

cat >"$tmp/rune.c" <<'EOF'
#include <stdio.h>
int main(void) {
    puts("{\"actions\":[],\"error\":null}");
    return 0;
}
EOF

[[ -r "$root/build-images/clang/rootfs.ext4" ]] ||   bash src/Rune.Firecracker/build-rootfs.sh build clang

descriptor="$(bash src/Rune.Firecracker/run-build-vm.sh clang c "$tmp/rune.c")"
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
echo "c build -> execute OK"

printf 'int main(void) { return missing; }\n' >"$tmp/invalid.c"
if output="$(bash src/Rune.Firecracker/run-build-vm.sh clang c "$tmp/invalid.c" 2>&1)"; then
  echo "c invalid source unexpectedly built" >&2
  exit 1
fi
grep -Fq "C compilation failed" <<<"$output"
grep -Fq "rune.c" <<<"$output"
! grep -Eq '/input/|/work/|Kernel panic|console=' <<<"$output"
echo "c diagnostics OK"


cat generated/c/rune_api.h >"$tmp/generated-rune.c"
cat >>"$tmp/generated-rune.c" <<'EOF'

#include <stdio.h>
#include <string.h>

static RestMessage fake_reply(
    void *context,
    const ReplyMessageProperties *reply_message
) {
    (void)context;

    RestMessage result = {
        .id = 99,
        .channel_id = 2,
        .content = reply_message->content,
        .author = {
            .id = 3,
            .username = "rune",
        },
    };

    return result;
}

int main(void) {
    Message message = {
        .id = 1,
        .channel_id = 2,
        .content = "hello",
        .author = {
            .id = 3,
            .username = "rune",
        },
    };

    RuneHost host = {
        .context = NULL,
        .message_reply = fake_reply,
    };

    ReplyMessageProperties properties = {
        .content = "generated",
    };

    RestMessage reply =
        rest_message_reply(&host, &properties);

    if (message.channel_id != 2) {
        return 1;
    }

    if (strcmp(reply.content, "generated") != 0) {
        return 1;
    }

    if (
        strcmp(
            REST_MESSAGE_REPLY_NETCORD,
            "NetCord.Rest.RestMessage.ReplyAsync"
        ) != 0
    ) {
        return 1;
    }

    puts("{\"actions\":[],\"error\":null}");
    return 0;
}
EOF

descriptor="$(bash src/Rune.Firecracker/run-build-vm.sh clang c "$tmp/generated-rune.c")"
read -r id _ _ <<<"$descriptor"
[[ "$id" == sha256:* ]]
digest="${id#sha256:}"
artifact="$root/artifacts/$digest"
test -s "$artifact"
response="$(bash src/Rune.Firecracker/run-invocation-vm.sh "$artifact" "$tmp/envelope.json")"

python3 - "$response" <<'PY'
import json
import sys

result = json.loads(sys.argv[1])
assert result == {"actions": [], "error": None}, result
PY

echo "generated c Rune API wrapper -> execute OK"
