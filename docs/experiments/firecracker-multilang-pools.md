# Firecracker multi-language pool experiment

Rune uses separate disposable build VMs and warm disposable invocation VMs.

| Language | Build pool | Invocation runtime | Artifact |
| --- | --- | --- | --- |
| JavaScript | ScriptC `--dynamic` | `native` | Linux executable |
| TypeScript | ScriptC `--dynamic` | `native` | Linux executable |
| Rust | rustc | `native` | Linux executable |
| C | clang | `native` | Linux executable |
| C++ | clang++ | `native` | Linux executable |
| C# | .NET Native AOT | `native` | Linux executable |
| Python | CPython validation | `python` | source |
| Ruby | Ruby validation | `ruby` | source |

There are only three invocation snapshot families: `native`, `python` and `ruby`. JavaScript, TypeScript, Rust, C, C++ and C# therefore share one autoscaled native pool. C# is published with Native AOT and no .NET runtime is present in invocation VMs.

Registration sends the source directly to `FirecrackerRuneBuilder`. The builder launches the matching isolated build VM, which has no network interface and has compiler-specific CPU, memory, process, file-descriptor, wall-time and writable-disk limits. Build dependencies are baked into the image. Source is attached read-only and is limited to 64 KiB.

A successful build produces one artifact in the bounded scratch filesystem. The host extracts it, rejects artifacts larger than 16 MiB, hashes it with SHA-256 and stores it by digest under the Firecracker artifact root. `RegisteredRune` stores only the resulting `BuiltRuneArtifact` descriptor for execution. Invocation envelopes contain the artifact descriptor and event payload, never source.

Before invocation, the runner resolves the content-addressed file and verifies both its size and SHA-256 digest. It then streams the artifact and invocation envelope over vsock into a fresh VM. The guest executes native artifacts directly, Python artifacts with CPython and Ruby artifacts with Ruby. Invocation VMs have no network interface, run the Rune as an unprivileged user and are destroyed after one invocation.

ScriptC belongs only in the build VM. Rune invokes it as `scriptc build <source> --dynamic -o <artifact>`. The build image contains Node 24+ and clang; the resulting invocation image contains neither Node nor ScriptC. Rune does not initially install user npm dependencies.

The remaining acceptance work is intentionally small: keep CI green, remove obsolete pre-Firecracker/old language-worker files, and exercise build + invocation on a Linux KVM host for all eight languages. The GitHub-hosted CI gate covers the protocol, resource policy, compiler/runtime routing, registration and artifact validation; the KVM smoke gate remains opt-in for a self-hosted runner.
