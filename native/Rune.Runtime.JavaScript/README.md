# Rune shared JavaScript runtime experiment

This crate is an isolated, native QuickJS reference implementation for the
shared-interpreter model. It is included in the workspace for tests and
benchmarks but is not wired into the bot or Rune's native C ABI.

See [`../../docs/experiments/shared-javascript-interpreter.md`](../../docs/experiments/shared-javascript-interpreter.md)
for the measurements, security trade-offs and proposed Wasmtime/Javy follow-up.
