<p align="center">
  <img src="assets/rune.png" alt="Rune" width="160">
</p>

<h1 align="center">Rune</h1>

<p align="center">
  A small, sandboxed scripting platform for Discord.
</p>

Rune builds uploaded scripts into executable artifacts and runs each invocation inside an isolated Firecracker microVM.

The current build pipeline supports JavaScript, TypeScript, Python, Rust, C, and C++. Rune.Api is a deliberately selected subset of NetCord.

## Source layout

Rune is a C#–Rust system. `src/` contains production source regardless of implementation language:

    src/
    ├── Rune.Api/                     generated C# API projection
    ├── Rune.Bot/                     Discord control plane (C#)
    ├── Rune.Runtime/                 build, Redis and dispatch control plane (C#)
    ├── Rune.Firecracker.Runner/      microVM runner (Rust)
    ├── Rune.Firecracker.Guest/       invocation guest init (Rust)
    ├── Rune.Firecracker.BuildGuest/  build guest init (Rust)
    └── Rune.Firecracker/              VM images, scripts and support tooling

Cross-component contracts live separately from implementations:

    contracts/
    ├── rune-api.yaml   script-facing Rune.Api SSOT
    └── runtime.yaml    C# ↔ Rust runtime protocol SSOT

`Rune.Generator` derives internal C# types/projections, Rust protocol types, TypeScript/Rust Rune.Api bindings and shared protocol constants from those contracts. Generated representations are not hand-maintained.

## Runtime architecture

Rune.Bot builds source through disposable Firecracker build VMs and stores content-addressed executable artifacts. Gateway events are projected into Rune.Api payloads and queued in Redis. Rune.Firecracker.Runner consumes those envelopes, invokes the artifact in a disposable microVM, and publishes host actions back through Redis. Rune.Bot applies those host actions to the original retained NetCord event object.

The runtime path is:

    Discord gateway event
    -> generated Rune.Api projection
    -> generated Redis invocation contract
    -> Rune.Firecracker.Runner
    -> disposable invocation microVM
    -> generated Redis result contract
    -> NetCord host action

## Running locally

You need:

- .NET 10
- Rust
- Redis
- Firecracker with KVM access
- the Rune Firecracker kernel/rootfs/snapshot assets
- the toolchains required for whichever Rune languages you build

The bot and runner must point at the same Redis instance and Firecracker state root.

    export RUNE_REDIS_URL=redis://127.0.0.1:6379/
    export RUNE_FIRECRACKER_ROOT="$HOME/.local/share/rune/firecracker"

    dotnet run --project src/Rune.Bot
    cargo run --manifest-path src/Rune.Firecracker.Runner/Cargo.toml

## Status

Rune is under active development.
