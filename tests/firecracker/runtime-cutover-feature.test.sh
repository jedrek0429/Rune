#!/usr/bin/env bash
set -euo pipefail

for path in   src/Rune.Runtime/Compilation   src/Rune.Runtime/Wasm   src/Rune.Runtime/RuneCompilationOptions.cs   examples/wasm.js; do
  if [[ -e "$path" ]]; then
    echo "legacy runtime path still exists: $path" >&2
    exit 1
  fi
done

if grep -RInE   'Extism|RuneExecutor|RuneWasmCache|AddRuneCompilation|wasm32-unknown-unknown|rune\.wasm|byte\[\][[:space:]]+Wasm'   src tests .github   --exclude-dir=bin   --exclude-dir=obj; then
  echo "legacy WebAssembly runtime reference remains" >&2
  exit 1
fi

grep -q 'IRuneBuilder, FirecrackerRuneBuilder'   src/Rune.Runtime/DependencyInjection.cs

grep -q 'IRuneTransport, RedisRuneTransport'   src/Rune.Runtime/DependencyInjection.cs

grep -q 'AddHostedService<RuneResultWorker>'   src/Rune.Bot/Program.cs

grep -q 'RuneInvocationReceiverRegistry'   src/Rune.Bot/Program.cs

echo 'microVM runtime cutover contract OK'
