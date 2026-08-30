# Firecracker multi-language pool experiment

This branch changes the Firecracker experiment from language-specific invocation VMs to two independent pool layers.

## Language matrix

| Rune language | Build pool | Invocation pool | Invocation artifact |
| --- | --- | --- | --- |
| JavaScript | ScriptC (`--dynamic`) | `native` | Linux executable |
| TypeScript | ScriptC (`--dynamic`) | `native` | Linux executable |
| Rust | Rust toolchain | `native` | Linux executable |
| C | clang | `native` | Linux executable |
| C++ | clang++ | `native` | Linux executable |
| Python | Python | `python` | validated source/package |
| Ruby | Ruby | `ruby` | validated source/package |
| C# | .NET SDK | `dotnet` | application assembly |

JavaScript and TypeScript no longer have a Node invocation runtime. ScriptC belongs only in the build VM. Its output is consumed by the same native invocation pool as Rust, C and C++.

## Pool boundaries

Build VMs are language/toolchain-specific because their dependency sets, image sizes and resource profiles are different. They are not reused for user invocations. A build VM receives generated Rune.API bindings plus source, produces an immutable artifact, hashes it, publishes it to the artifact store and is destroyed.

Invocation VMs are runtime-specific. The runner maintains only four warm snapshot families: `native`, `python`, `ruby` and `dotnet`. The `native` backlog is the sum of the JavaScript, TypeScript, Rust, C and C++ invocation streams, so those languages genuinely share one autoscaled pool.

Every invocation still uses a disposable VM restored from a clean snapshot. A VM is destroyed after one invocation and replenished from the matching warm snapshot.

## Protocol cutover

`InvocationEnvelope` now has an optional `artifact` descriptor with `id`, `digest` and `entrypoint`. `source` remains temporarily for compatibility with the current Discord/Redis producer while the build service is introduced. The intended cutover is:

1. registration sends source to a build queue;
2. a build worker compiles/validates it and returns a `BuiltRuneArtifact`;
3. the registry stores the artifact descriptor instead of executable source;
4. event invocations contain only the artifact descriptor and event payload;
5. the invocation runner resolves the artifact into the disposable VM and verifies its digest before execution;
6. remove `source` from `InvocationEnvelope`.

Artifact IDs must be opaque identifiers resolved by the host. User-controlled filesystem paths must never be accepted as artifact locations.

## ScriptC

Rune should invoke ScriptC as roughly:

```sh
scriptc build rune.ts --dynamic -o /out/rune
```

The build image can contain Node and clang because they are compiler dependencies. The resulting invocation image must not contain Node. JavaScript and TypeScript are therefore native invocation languages even when ScriptC uses its embedded dynamic tier.

Rune does not initially provide npm installation. `--dynamic` is allowed because it is also useful for JavaScript constructs that ScriptC cannot lower statically; it is not a promise of Node/npm compatibility.

## Remaining implementation work

This first slice changes the runner's language/runtime model, shared autoscaling and artifact protocol. Before the experiment is runnable end-to-end, the branch still needs: build queue/service; four invocation rootfs/snapshot images; artifact transport into the guest; native/Python/Ruby/.NET guest launchers; generated Rune.API bindings for the new languages; registration cutover; and integration tests that build and invoke a minimal Rune in every language.

The branch should stay a draft experiment until those pieces are complete. It deliberately does not pretend that adding enum values equals language support.
