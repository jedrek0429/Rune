#!/usr/bin/env bash
set -euo pipefail

[[ $# -eq 2 ]] || { echo "usage: $0 <artifact> <envelope.json>" >&2; exit 2; }
artifact="$1"
envelope="$2"
root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
firecracker="${RUNE_FIRECRACKER:-firecracker}"
kernel="${RUNE_KERNEL:-$root/vmlinux}"
rootfs="$root/images/rune/rootfs.ext4"
tmp="$(mktemp -d)"
api_sock="$tmp/firecracker.sock"
vsock_sock="$tmp/vsock.sock"
console_log="$tmp/console.log"
pid=""

cleanup() {
  if [[ -n "$pid" ]]; then
    kill "$pid" >/dev/null 2>&1 || true
    wait "$pid" 2>/dev/null || true
  fi
  rm -rf "$tmp"
}

on_exit() {
  local status=$?
  trap - EXIT
  if (( status != 0 )) && [[ -f "$console_log" ]]; then
    echo "--- Firecracker guest console ---" >&2
    cat "$console_log" >&2 || true
    echo "--- end guest console ---" >&2
  fi
  cleanup
  exit "$status"
}
trap on_exit EXIT

for dependency in curl python3 "$firecracker"; do
  command -v "$dependency" >/dev/null 2>&1 || { echo "missing dependency: $dependency" >&2; exit 1; }
done
[[ -r "$artifact" ]] || { echo "missing artifact: $artifact" >&2; exit 1; }
[[ -r "$envelope" ]] || { echo "missing envelope: $envelope" >&2; exit 1; }
[[ -r "$kernel" ]] || { echo "missing kernel: $kernel" >&2; exit 1; }
[[ -r "$rootfs" ]] || { echo "missing invocation rootfs: $rootfs" >&2; exit 1; }
artifact_size="$(stat -c %s "$artifact")"
(( artifact_size > 0 && artifact_size <= 16 * 1024 * 1024 )) || { echo "invalid artifact size" >&2; exit 1; }

"$firecracker" --api-sock "$api_sock" >"$console_log" 2>&1 &
pid=$!
for _ in $(seq 1 400); do
  [[ -S "$api_sock" ]] && break
  kill -0 "$pid" 2>/dev/null || exit 1
  sleep 0.01
done
[[ -S "$api_sock" ]] || { echo "Firecracker API socket did not appear" >&2; exit 1; }

json_string() { python3 -c 'import json,sys; print(json.dumps(sys.argv[1]))' "$1"; }
api_put() {
  local endpoint="$1" body="$2" response status
  response="$(mktemp "$tmp/api.XXXXXX")"
  status="$(curl --silent --show-error --unix-socket "$api_sock" -o "$response" -w '%{http_code}' \
    -X PUT -H 'Content-Type: application/json' -d "$body" "http://localhost$endpoint")"
  if [[ "$status" != 2* ]]; then
    echo "Firecracker PUT $endpoint failed ($status): $(cat "$response")" >&2
    return 1
  fi
}

api_put /machine-config '{"vcpu_count":1,"mem_size_mib":192,"smt":false}'
api_put /boot-source "{\"kernel_image_path\":$(json_string "$kernel"),\"boot_args\":\"console=ttyS0 reboot=k panic=1 pci=off root=/dev/vda ro init=/sbin/rune-guest\"}"
api_put /drives/rootfs "{\"drive_id\":\"rootfs\",\"path_on_host\":$(json_string "$rootfs"),\"is_root_device\":true,\"is_read_only\":true}"
api_put /vsock "{\"guest_cid\":3,\"uds_path\":$(json_string "$vsock_sock")}"
# Deliberately no network interface.
api_put /actions '{"action_type":"InstanceStart"}'

for _ in $(seq 1 400); do
  grep -q 'RUNE_READY' "$console_log" && break
  kill -0 "$pid" 2>/dev/null || exit 1
  sleep 0.01
done
grep -q 'RUNE_READY' "$console_log" || { echo "guest did not become ready" >&2; exit 1; }

python3 - "$vsock_sock" "$artifact" "$envelope" <<'PY'
import socket, struct, sys
sock_path, artifact_path, envelope_path = sys.argv[1:]
artifact = open(artifact_path, 'rb').read()
envelope = open(envelope_path, 'rb').read()
if not envelope or len(envelope) > 128 * 1024:
    raise SystemExit('invalid invocation envelope size')
s = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
s.settimeout(5)
try:
    s.connect(sock_path)
    s.sendall(b'CONNECT 5000\n')
    reply = b''
    while not reply.endswith(b'\n'):
        chunk = s.recv(1)
        if not chunk:
            raise SystemExit('vsock proxy closed during handshake')
        reply += chunk
    if not reply.startswith(b'OK '):
        raise SystemExit(f'vsock handshake failed: {reply!r}')
    s.sendall(struct.pack('>Q', len(artifact)))
    s.sendall(artifact)
    s.sendall(struct.pack('>I', len(envelope)))
    s.sendall(envelope)
    response = b''
    while not response.endswith(b'\n'):
        chunk = s.recv(4096)
        if not chunk:
            break
        response += chunk
except socket.timeout:
    raise SystemExit('guest response timed out')
finally:
    s.close()
if not response:
    raise SystemExit('guest returned no response')
sys.stdout.buffer.write(response)
PY
