<p align="center">
  <img src="assets/rune.png" alt="Rune" width="160">
</p>

<h1 align="center">Rune</h1>

<p align="center">A small, sandboxed scripting platform for Discord.</p>

Rune lets guilds upload event scripts through Discord. Registration builds each script in a disposable Firecracker microVM; invocations run the resulting immutable artifact in a fresh microVM restored from a warm snapshot.

Supported languages are JavaScript, TypeScript, Python, Ruby, Rust, C, C++ and C#. JavaScript and TypeScript compile with ScriptC, C# uses .NET Native AOT, and all native outputs share one invocation pool. Python and Ruby use their own interpreter pools.

Rune.API is a deliberately selected subset of NetCord. Guest VMs have no general network access; host-backed Discord operations cross a narrow Rune.API boundary.

## Runtime

```text
source
  -> isolated build VM
  -> content-addressed artifact
  -> Redis invocation
  -> native | python | ruby warm pool
  -> disposable invocation VM
```

The Discord token and NetCord client remain on the host. Source is limited to 64 KiB and never travels to invocation VMs. Artifacts are limited to 16 MiB and verified by SHA-256 before execution.

## Development

The bot requires .NET 10 and Redis. Firecracker runners require Linux with KVM. Docker is used to build the compiler and invocation root filesystems.

```sh
./firecracker/check-host.sh
sudo ./firecracker/build-all.sh
dotnet run --project src/Rune.Bot
cargo run --release --manifest-path native/Rune.Firecracker.Runner/Cargo.toml
```

The Firecracker integration is experimental. GitHub CI covers the managed and Rust contracts and resource-policy tests; full microVM smoke tests require a Linux runner with `/dev/kvm`.
