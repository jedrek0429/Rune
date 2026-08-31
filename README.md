<p align="center">
  <img src="assets/rune.png" alt="Rune" width="160">
</p>

<h1 align="center">Rune</h1>

<p align="center">A small, sandboxed scripting platform for Discord.</p>

Rune lets guilds upload event scripts through Discord. Registration builds each script in a disposable Firecracker microVM; invocations run the resulting immutable executable in a fresh microVM restored from one warm snapshot.

Supported languages are JavaScript, TypeScript, Python, Ruby, Rust, C, C++ and C#. JavaScript and TypeScript use ScriptC, C# uses .NET Native AOT, Python packages precompiled MicroPython bytecode into an executable, and Ruby packages mruby bytecode into an executable.

Every build therefore ends at the same boundary: a self-contained Linux executable implementing the Rune execution ABI. The execution plane does not know which language produced it.

Rune.API is a deliberately selected subset of NetCord. Guest VMs have no general network access; host-backed Discord operations cross a narrow Rune.API boundary.

## Runtime

```text
source
  -> language-specific isolated build VM
  -> content-addressed executable
  -> rune:invocations
  -> one autoscaled warm Firecracker pool
  -> disposable invocation VM
```

The Discord token and NetCord client remain on the host. Source is limited to 64 KiB and never travels to invocation VMs. Artifacts are limited to 16 MiB and verified by SHA-256 before execution.

## Development

The bot requires .NET 10 and Redis. Firecracker runners require Linux with KVM. Docker is used to build compiler and invocation root filesystems.

```sh
./firecracker/check-host.sh
sudo ./firecracker/build-all.sh
dotnet run --project src/Rune.Bot
cargo run --release --manifest-path native/Rune.Firecracker.Runner/Cargo.toml
```

GitHub CI runs the managed, Rust, resource-policy and real KVM end-to-end checks for all eight languages.
