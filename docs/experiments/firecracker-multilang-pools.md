# Firecracker multi-language build experiment

Rune uses language-specific disposable build VMs and one language-agnostic disposable invocation pool.

| Language | Build pool | Artifact |
| --- | --- | --- |
| JavaScript | ScriptC `--dynamic` | Linux executable |
| TypeScript | ScriptC `--dynamic` | Linux executable |
| Rust | rustc | Linux executable |
| C | clang | Linux executable |
| C++ | clang++ | Linux executable |
| C# | .NET Native AOT | Linux executable |
| Python | `mpy-cross` + embedded MicroPython | Linux executable |
| Ruby | `mrbc` + embedded mruby | Linux executable |

The build boundary is the execution contract. Every accepted Rune becomes a self-contained executable that reads the invocation envelope from stdin and writes a Rune result to stdout. Source language is build metadata only; it is absent from invocation and result envelopes.

Registration sends source to the matching isolated build VM. Build VMs have no network interface and have compiler-specific CPU, memory, process, file-descriptor, wall-time and writable-disk limits. Source is attached read-only and limited to 64 KiB.

Python is precompiled with `mpy-cross`; its `.mpy` bytecode is embedded with the MicroPython embed port into the final executable. Ruby is compiled with `mrbc`; its bytecode is linked with mruby into the final executable. Neither interpreter exists in the invocation image.

A successful build produces one executable in the bounded scratch filesystem. The host rejects artifacts larger than 16 MiB, hashes them with SHA-256 and stores them by digest. `RegisteredRune` retains the source language for rebuilds plus the resulting `BuiltRuneArtifact` descriptor.

All invocation envelopes enter the single `rune:invocations` Redis stream. The runner verifies the artifact size and digest, restores a VM from the single warm snapshot, streams the executable and invocation envelope over vsock, and executes `/tmp/rune-artifact` directly as an unprivileged user. The VM has no network interface and is destroyed after one invocation.

The Redis backlog therefore scales one fungible VM pool. There is no language routing, interpreter routing or per-language snapshot balancing in the execution plane.

ScriptC, `mpy-cross`, MicroPython, mruby, rustc, clang and the .NET SDK exist only in build images. The invocation rootfs contains the Rune guest plus the native system libraries required by produced executables.

The acceptance gate is a real KVM end-to-end test that builds all eight languages into executable artifacts and invokes all eight through the same Redis stream and warm Firecracker snapshot.
