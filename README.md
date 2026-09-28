<p align="center">
  <img src="assets/rune.png" alt="Rune" width="160">
</p>

<h1 align="center">Rune</h1>

<p align="center">
  A small, sandboxed scripting platform for Discord.
</p>

Rune builds uploaded scripts into executable artifacts and runs each invocation inside an isolated Firecracker microVM.

The current build pipeline supports JavaScript, TypeScript, Python, Rust, C, and C++. Rune.Api is a deliberately selected subset of NetCord and currently defines four gateway events:

- MessageCreate
- MessageDelete
- MessageReactionAdd
- MessageReactionRemove

Rune.Api bindings are generated from the same canonical API definition for each language.

## Runtime architecture

Rune.Bot builds source through disposable Firecracker build VMs and stores content-addressed executable artifacts. Gateway events are projected into Rune.Api payloads and queued in Redis. Rune.Firecracker.Runner consumes those envelopes, invokes the artifact in a disposable microVM, and publishes host actions back through Redis. Rune.Bot applies those host actions to the original retained NetCord event object.

The runtime path is therefore:

    Discord gateway event
    -> Rune.Api projection
    -> Redis invocation envelope
    -> Firecracker runner
    -> disposable invocation microVM
    -> Redis result
    -> NetCord host action

## Running locally

You need:

- .NET 10
- Redis
- Firecracker with KVM access
- the Rune Firecracker kernel/rootfs/snapshot assets
- the toolchains required for whichever Rune languages you build

The bot and native runner must point at the same Redis instance and Firecracker state root.

    export RUNE_REDIS_URL=redis://127.0.0.1:6379/
    export RUNE_FIRECRACKER_ROOT="$HOME/.local/share/rune/firecracker"

    dotnet run --project src/Rune.Bot
    cargo run --manifest-path native/Rune.Firecracker.Runner/Cargo.toml

## Status

Rune is under active development.