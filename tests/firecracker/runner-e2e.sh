#!/usr/bin/env bash
set -euo pipefail

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
tmp="$(mktemp -d)"
runner_pid=""
redis_pid=""

cleanup() {
  if [[ -n "$runner_pid" ]]; then
    kill "$runner_pid" >/dev/null 2>&1 || true
    wait "$runner_pid" 2>/dev/null || true
  fi

  if [[ -n "$redis_pid" ]]; then
    kill "$redis_pid" >/dev/null 2>&1 || true
    wait "$redis_pid" 2>/dev/null || true
  fi

  rm -rf "$tmp"
}
trap cleanup EXIT

for dependency in   cargo   cc   redis-cli   redis-server   sha256sum; do
  command -v "$dependency" >/dev/null 2>&1 || {
    echo "missing dependency: $dependency" >&2
    exit 1
  }
done

redis-server   --bind 127.0.0.1   --port 6379   --save ""   --appendonly no   --daemonize no   >"$tmp/redis.log" 2>&1 &
redis_pid=$!

for _ in $(seq 1 100); do
  if redis-cli -h 127.0.0.1 -p 6379 ping       2>/dev/null | grep -q PONG; then
    break
  fi
  sleep 0.05
done

redis-cli -h 127.0.0.1 -p 6379 ping   | grep -q PONG

bash firecracker/build-snapshot.sh

cat >"$tmp/rune.c" <<'C'
#include <stdio.h>

int main(void) {
    char buffer[4096];

    while (fread(buffer, 1, sizeof buffer, stdin) != 0) {
    }

    fputs(
        "{\"actions\":[{\"method\":\"message.reply\","
        "\"arguments\":{\"replyMessage\":{\"content\":"
        "\"redis-runner-e2e\"}}}],\"error\":null}\n",
        stdout
    );

    return 0;
}
C

cc -O2 "$tmp/rune.c" -o "$tmp/rune"

digest="$(sha256sum "$tmp/rune" | cut -d' ' -f1)"
size="$(wc -c <"$tmp/rune")"

mkdir -p "$root/artifacts"
install -m 0444   "$tmp/rune"   "$root/artifacts/$digest"

cargo build   --quiet   --release   --manifest-path   native/Rune.Firecracker.Runner/Cargo.toml

RUNE_REDIS_URL="redis://127.0.0.1:6379/" RUNE_FIRECRACKER_ROOT="$root" RUNE_VM_MIN=1 RUNE_VM_MAX=1 RUNE_RUNNER_NAME=e2e RUST_LOG=rune_firecracker_runner=info   target/release/rune-firecracker-runner   >"$tmp/runner.log" 2>&1 &
runner_pid=$!

for _ in $(seq 1 400); do
  grep -q 'Rune Firecracker runner ready'     "$tmp/runner.log" &&
    break

  kill -0 "$runner_pid" 2>/dev/null || {
    cat "$tmp/runner.log" >&2
    exit 1
  }

  sleep 0.05
done

grep -q 'Rune Firecracker runner ready'   "$tmp/runner.log" || {
    cat "$tmp/runner.log" >&2
    exit 1
  }

envelope="$(
  python3 - "$digest" "$size" <<'PY'
import json
import sys

digest = sys.argv[1]
size = int(sys.argv[2])

print(json.dumps({
    "executionId": "00000000-0000-0000-0000-000000000011",
    "invocationId": "00000000-0000-0000-0000-000000000012",
    "runeId": "00000000-0000-0000-0000-000000000013",
    "runeName": "redis-runner-e2e",
    "guildId": 1,
    "eventType": "messageCreate",
    "artifact": {
        "id": f"sha256:{digest}",
        "digest": f"sha256:{digest}",
        "entrypoint": "rune",
        "sizeBytes": size,
    },
    "payload": {
        "id": "3",
        "channelId": "2",
        "content": "hello",
        "author": {
            "id": "4",
            "username": "rune",
        },
    },
    "enqueuedAt": "2026-09-28T00:00:00Z",
}))
PY
)"

redis-cli   -h 127.0.0.1   -p 6379   XADD rune:invocations '*'   json "$envelope"   >/dev/null

for _ in $(seq 1 400); do
  count="$(
    redis-cli       -h 127.0.0.1       -p 6379       XLEN rune:results
  )"

  if [[ "$count" -gt 0 ]]; then
    break
  fi

  kill -0 "$runner_pid" 2>/dev/null || {
    cat "$tmp/runner.log" >&2
    exit 1
  }

  sleep 0.05
done

result="$(
  redis-cli     -h 127.0.0.1     -p 6379     --raw     XRANGE rune:results - + COUNT 1
)"

grep -q '"error":null' <<<"$result"
grep -q '"method":"message.reply"' <<<"$result"
grep -q '"content":"redis-runner-e2e"' <<<"$result"

remaining="$(
  redis-cli     -h 127.0.0.1     -p 6379     XLEN rune:invocations
)"

[[ "$remaining" -eq 0 ]]

echo 'Redis -> runner -> Firecracker -> result e2e OK'
