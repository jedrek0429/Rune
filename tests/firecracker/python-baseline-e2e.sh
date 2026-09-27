#!/usr/bin/env bash
set -euo pipefail

root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

cat >"$tmp/envelope.json" <<'EOF'
{"executionId":"e","invocationId":"i","runeId":"r","runeName":"python-baseline","guildId":1,"eventType":"messageCreate","artifact":{"id":"unused","digest":"unused","entrypoint":"rune","sizeBytes":1},"payload":{},"enqueuedAt":"2026-09-27T00:00:00Z"}
EOF

cat >"$tmp/rune.py" <<'EOF'
import json
import math
import collections
import io
import struct
import re
import random
import time

assert json.loads('{"value": 42}')["value"] == 42
assert math.sqrt(81) == 9

queue = collections.deque([1, 2, 3], 3)
assert queue.popleft() == 1

buffer = io.StringIO()
buffer.write("rune")
assert buffer.getvalue() == "rune"

packed = struct.pack(">H", 0x1234)
assert struct.unpack(">H", packed)[0] == 0x1234

assert re.match("r.ne", "rune") is not None

random.seed(123)
value = random.randint(1, 10)
assert 1 <= value <= 10

# Float literal and runtime-created float.
literal_float = 123.456
runtime_float = float("123.456")

assert literal_float > 123
assert literal_float < 124
assert runtime_float > 123
assert runtime_float < 124

# Large integers / MPZ.
large = 10**30
assert large > 2**63

# POSIX wall-clock time.
now = time.time()
assert isinstance(now, (int, float))
assert now > 0

now_ns = time.time_ns()
assert isinstance(now_ns, int)
assert now_ns > 0
assert abs(now_ns / 1_000_000_000 - now) < 2

# Epoch must be Unix epoch.
utc_epoch = time.gmtime(0)
assert utc_epoch[0] == 1970
assert utc_epoch[1] == 1
assert utc_epoch[2] == 1
assert utc_epoch[3] == 0
assert utc_epoch[4] == 0
assert utc_epoch[5] == 0

# Current local/UTC conversion should return a valid time tuple.
local = time.localtime(now)
assert len(local) >= 8

# mktime/localtime round-trip, allowing integer precision.
round_trip = time.mktime(local)
assert isinstance(round_trip, (int, float))
assert abs(round_trip - now) < 2

# Monotonic tick functions.
before_ms = time.ticks_ms()
before_us = time.ticks_us()

time.sleep(0.01)

after_ms = time.ticks_ms()
after_us = time.ticks_us()

assert time.ticks_diff(after_ms, before_ms) >= 0
assert time.ticks_diff(after_us, before_us) >= 0

assert time.ticks_add(before_ms, 10) != before_ms

# Confirm normal bytes constants still work through .mpy loading.
blob = b"\x01\x02\x03\x04"
assert len(blob) == 4
assert blob[2] == 3

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
import json
import sys

result = json.loads(sys.argv[1])
assert result == {"actions": [], "error": None}, result
PY

echo "python baseline libraries -> execute OK"
