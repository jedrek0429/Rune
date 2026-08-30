#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
root="${RUNE_FIRECRACKER_ROOT:-/var/lib/rune/firecracker}"
tmp="$(mktemp -d)"
runner_pid=""
own_redis=0

cleanup() {
  if [[ -n "$runner_pid" ]]; then
    kill "$runner_pid" >/dev/null 2>&1 || true
    wait "$runner_pid" 2>/dev/null || true
  fi
  if [[ "$own_redis" -eq 1 ]]; then
    redis-cli shutdown nosave >/dev/null 2>&1 || true
  fi
  rm -rf "$tmp"
}
trap cleanup EXIT

for command in cargo docker firecracker redis-cli redis-server; do
  command -v "$command" >/dev/null || { echo "missing dependency: $command" >&2; exit 1; }
done
[[ -r /dev/kvm && -w /dev/kvm ]] || { echo "/dev/kvm is required for Firecracker e2e tests" >&2; exit 1; }
[[ -r "$root/vmlinux" ]] || { echo "missing Firecracker kernel: $root/vmlinux" >&2; exit 1; }

probe_source() {
  case "$1" in
  javascript)
    printf '%s\n' "console.log('{\"actions\":[],\"error\":null,\"durationMicros\":1}');"
    ;;
  typescript)
    printf '%s\n' "const result: string = '{\"actions\":[],\"error\":null,\"durationMicros\":1}'; console.log(result);"
    ;;
  python)
    printf '%s\n' "print('{\"actions\":[],\"error\":null,\"durationMicros\":1}')"
    ;;
  ruby)
    printf '%s\n' "puts '{\"actions\":[],\"error\":null,\"durationMicros\":1}'"
    ;;
  rust)
    printf '%s\n' 'fn main() { println!(r#"{"actions":[],"error":null,"durationMicros":1}"#); }'
    ;;
  c)
    printf '%s\n' '#include <stdio.h>' 'int main(void) { puts("{\\"actions\\":[],\\"error\\":null,\\"durationMicros\\":1}"); return 0; }'
    ;;
  cpp)
    printf '%s\n' '#include <iostream>' 'int main() { std::cout << "{\\"actions\\":[],\\"error\\":null,\\"durationMicros\\":1}\\n"; }'
    ;;
  csharp)
    printf '%s\n' 'Console.WriteLine("{\\"actions\\":[],\\"error\\":null,\\"durationMicros\\":1}");'
    ;;
  *) return 2 ;;
  esac
}

build_target() {
  case "$1" in
  javascript) echo 'scriptc javascript' ;;
  typescript) echo 'scriptc typescript' ;;
  python) echo 'python python' ;;
  ruby) echo 'ruby ruby' ;;
  rust) echo 'rust rust' ;;
  c) echo 'clang c' ;;
  cpp) echo 'clang cpp' ;;
  csharp) echo 'dotnet-aot csharp' ;;
  *) return 2 ;;
  esac
}

wire_language() {
  case "$1" in
  javascript) echo javaScript ;;
  typescript) echo typeScript ;;
  csharp) echo cSharp ;;
  *) echo "$1" ;;
  esac
}

mkdir -p "$root"
for runtime in native python ruby; do
  bash "$repo_root/firecracker/build-rootfs.sh" invocation "$runtime"
  bash "$repo_root/firecracker/build-snapshot.sh" "$runtime"
done

declare -A artifact_id artifact_size
for pool in scriptc clang rust dotnet-aot python ruby; do
  bash "$repo_root/firecracker/build-rootfs.sh" build "$pool"
  for language in javascript typescript python ruby rust c cpp csharp; do
    read -r language_pool wire <<<"$(build_target "$language")"
    [[ "$language_pool" == "$pool" ]] || continue
    source="$tmp/$language"
    probe_source "$language" >"$source"
    read -r id size entrypoint < <(bash "$repo_root/firecracker/run-build-vm.sh" "$pool" "$wire" "$source")
    [[ "$entrypoint" == rune ]] || { echo "$language build returned wrong entrypoint" >&2; exit 1; }
    artifact_id[$language]="$id"
    artifact_size[$language]="$size"
  done
  rm -f "$root/build-images/$pool/rootfs.ext4"
  docker image rm -f "rune-firecracker-build-$pool:dev" >/dev/null 2>&1 || true
done

if redis-cli ping >/dev/null 2>&1; then
  redis-cli flushdb >/dev/null
else
  redis-server --save '' --appendonly no --daemonize yes
  own_redis=1
fi

(
  cd "$repo_root"
  RUNE_FIRECRACKER_ROOT="$root" \
  RUNE_VM_MIN=1 RUNE_VM_MAX=2 \
  cargo run --release -p rune-firecracker-runner
) >"$tmp/runner.log" 2>&1 &
runner_pid=$!

ready=0
for _ in $(seq 1 600); do
  if grep -q 'Rune Firecracker runner is ready' "$tmp/runner.log"; then
    ready=1
    break
  fi
  kill -0 "$runner_pid" 2>/dev/null || { cat "$tmp/runner.log" >&2; exit 1; }
  sleep 0.1
done
[[ "$ready" -eq 1 ]] || { cat "$tmp/runner.log" >&2; echo "runner did not become ready" >&2; exit 1; }

for language in javascript typescript python ruby rust c cpp csharp; do
  execution_id="e2e-$language"
  wire="$(wire_language "$language")"
  json="$(python3 - "$execution_id" "$wire" "${artifact_id[$language]}" "${artifact_size[$language]}" <<'PY'
import json, sys
execution, language, artifact, size = sys.argv[1:]
print(json.dumps({
    "executionId": execution,
    "invocationId": execution,
    "runeId": execution,
    "runeName": execution,
    "guildId": 1,
    "language": language,
    "eventType": "messageCreate",
    "artifact": {"id": artifact, "digest": artifact, "entrypoint": "rune", "sizeBytes": int(size)},
    "payload": {"content": "firecracker e2e"},
    "enqueuedAt": "2026-08-30T00:00:00Z"
}, separators=(",", ":")))
PY
)"
  redis-cli XADD "rune:invocations:$language" '*' json "$json" >/dev/null

  result=""
  for _ in $(seq 1 300); do
    result="$(redis-cli --raw XRANGE rune:results - + | grep -F "\"executionId\":\"$execution_id\"" | tail -1 || true)"
    [[ -z "$result" ]] || break
    kill -0 "$runner_pid" 2>/dev/null || { cat "$tmp/runner.log" >&2; exit 1; }
    sleep 0.1
  done
  [[ -n "$result" ]] || { cat "$tmp/runner.log" >&2; echo "$language produced no result" >&2; exit 1; }
  python3 - "$language" "$result" <<'PY'
import json, sys
language, raw = sys.argv[1:]
result = json.loads(raw)
if result.get("error") is not None:
    raise SystemExit(f"{language} failed: {result['error']}")
PY
  echo "$language e2e passed"
done

echo "all eight Firecracker language paths passed"
