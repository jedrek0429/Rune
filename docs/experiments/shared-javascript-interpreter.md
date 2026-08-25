# Shared JavaScript interpreter experiment

Status: experimental. This branch does not replace Rune's working Component
backend.

## Question

The current JavaScript backend asks `jco componentize` to produce one
self-contained Component per rune. Each Component contains a JavaScript
interpreter, so a tiny script pays the interpreter's compilation, storage,
memory and Wasmtime fuel costs again.

This experiment asks whether Rune can load one precompiled JavaScript engine,
store tiny scripts, and create isolated execution state only when a rune is
invoked.

## Native QuickJS reference implementation

`native/Rune.Runtime.JavaScript` embeds QuickJS through `rquickjs`. One
`SharedJavaScriptRuntime` owns the interpreter and a registry of rune sources.
Registration parses the source and then stores it. Invocation creates a fresh
QuickJS context, projects the selected event payload into read-only JavaScript
objects, executes the source, collects bounded host actions, destroys the
context and runs garbage collection.

The executable contract proves:

- invalid source cannot replace a working rune;
- all four selected gateway event payloads preserve 64-bit snowflakes;
- `message.reply(...)` produces a bounded host action;
- event payloads and nested values are read-only;
- every invocation receives fresh global state;
- runes cannot observe globals or modified intrinsics from another rune;
- `fetch`, `process` and `require` are absent;
- wall-clock interruption stops an infinite loop;
- the memory limit contains an allocation bomb;
- output limits discard partial actions; and
- a healthy rune still executes after timeout or memory exhaustion.

Run it with:

```sh
cargo test -p rune-runtime-javascript-experiment
cargo clippy -p rune-runtime-javascript-experiment --all-targets -- -D warnings
cargo run --release -p rune-runtime-javascript-experiment \
  --bin shared-js-benchmark -- 1000
```

## Measurements

Measurements were taken on 25 August 2026 in the Linux x86-64 development
workspace. They describe the relative architecture cost, rather than a
cross-machine performance promise.

| Model | Registration | Per-rune artefact | Invocation | Shared engine |
| --- | ---: | ---: | ---: | ---: |
| Current `jco` self-contained Component | 3.28–3.58 s | about 12.46 MB | Exceeds the current 50 million fuel budget on the test laptop | None |
| Native shared QuickJS prototype | mean 0.123 ms | source only | mean 0.123 ms with a fresh context | about 2.02 MB benchmark binary; 91.5 KB live QuickJS heap after 1,000 runes |
| Javy 9.1 dynamic module | 0.428–0.524 s through the external CLI | 715 bytes | Not measured yet | 1.34 MB Wasm plugin |

The native benchmark used 1,000 separately registered runes. Its registration
median was 0.115 ms and invocation median was 0.110 ms. The current `jco`
fixture is in `native/Rune.Runtime.JavaScript/fixtures` so the comparison can be
repeated.

Javy's dynamic mode is especially relevant. It produces small Wasm modules
which import a shared QuickJS provider instead of embedding QuickJS in every
rune. This is the same performance shape Rune needs while retaining Wasmtime as
the security boundary.

## Security conclusion

The native prototype proves that shared source registration and fresh contexts
are fast enough. Embedding QuickJS directly in Rune's native process would,
however, make a QuickJS memory-safety failure a Rune host compromise. The
current prototype also serialises JavaScript execution through one shared
runtime lock.

The preferred production experiment is therefore:

```text
one Wasmtime Engine
  one compiled Rune JavaScript provider module
    one fresh Store and provider instance per invocation
      one tiny rune module or bytecode payload
```

Wasmtime compiles the provider code once. Each invocation receives fresh linear
memory and QuickJS state, and Rune applies the same wall-clock, memory, output
and host-call limits used by other languages. Javy's custom plugin mechanism can
expose only generated Rune.API host calls and can target `wasm32-unknown-unknown`,
so it does not require general WASI access.

Fuel remains a secondary counter because interpreter instructions do not map
fairly to directly compiled Rust instructions. A common wall-clock deadline and
memory limit remain the language-neutral security budget.

## Next experiment

Build a minimal custom Javy provider which:

1. imports only one Rune test host call;
2. accepts source or precompiled QuickJS bytecode without spawning the Javy CLI;
3. runs two runes in separate Wasmtime Stores;
4. proves state, memory and timeout isolation; and
5. measures provider instantiation and warm invocation.

If direct source compilation inside the provider keeps registration below a
practical Discord interaction deadline, it should replace the self-contained
JavaScript Components. The native QuickJS crate remains a behavioural reference
and fallback for an isolated worker-process design.

## Sources

- [Javy project](https://github.com/bytecodealliance/javy)
- [Javy dynamic linking](https://github.com/bytecodealliance/javy/blob/main/docs/docs-using-dynamic-linking.md)
- [Javy custom plugins](https://github.com/bytecodealliance/javy/blob/main/docs/docs-using-extending.md)
- [RQuickJS](https://github.com/DelSkayn/rquickjs)
