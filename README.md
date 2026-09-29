<p align="center">
  <img src="assets/rune.png" alt="Rune" width="160">
</p>

<h1 align="center">Rune</h1>

<p align="center">
  A small, sandboxed scripting platform for Discord.
</p>

Rune builds uploaded scripts into executable artifacts and runs each invocation inside an isolated Firecracker microVM.

The current build pipeline supports JavaScript, TypeScript, Python, Rust, C, and C++. Rune API is a deliberately selected subset of NetCord.

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

`Rune.Generator` derives internal C# types/projections, Rust protocol types, JavaScript/TypeScript/Rust Rune API bindings and shared protocol constants from those contracts. Generated representations are not hand-maintained.

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

Rune currently runs on Linux with KVM. Local development uses Firecracker microVMs for both Rune compilation and execution.

### Requirements

Install:

- .NET 10 SDK
- Rust
- Docker
- Redis
- `curl`
- `python3`
- `e2fsprogs`
- KVM support through `/dev/kvm`

Check that KVM is available:

```bash
test -e /dev/kvm && echo "KVM available"
```

Your user must be able to access `/dev/kvm`. On development machines, you can temporarily allow access with:

```bash
sudo chmod a+rw /dev/kvm
```

### 1. Configure local state

Rune stores Firecracker images, compiled Rune artifacts, the kernel and snapshots under `RUNE_FIRECRACKER_ROOT`.

Use a directory owned by your user:

```bash
export RUNE_FIRECRACKER_ROOT="$HOME/.local/share/rune/firecracker"
mkdir -p "$RUNE_FIRECRACKER_ROOT"
```

To keep this setting between shells:

```bash
echo 'export RUNE_FIRECRACKER_ROOT="$HOME/.local/share/rune/firecracker"' >> ~/.zshrc
source ~/.zshrc
```

Rune uses Redis for communication between the bot and the microVM runner:

```bash
export RUNE_REDIS_URL="redis://127.0.0.1:6379/"
```

### 2. Generate Rune contracts

Generate the files derived from the Rune API and runtime contracts:

```bash
dotnet run --project tools/Rune.Generator --configuration Release -- generate
```

### 3. Install Firecracker

Rune currently uses Firecracker `v1.16.1`.

For an `x86_64` machine:

```bash
FIRECRACKER_VERSION=v1.16.1
ARCH="$(uname -m)"

curl -fsSL \
  "https://github.com/firecracker-microvm/firecracker/releases/download/${FIRECRACKER_VERSION}/firecracker-${FIRECRACKER_VERSION}-${ARCH}.tgz" \
  | tar -xz

mkdir -p "$HOME/.local/bin"

install \
  "release-${FIRECRACKER_VERSION}-${ARCH}/firecracker-${FIRECRACKER_VERSION}-${ARCH}" \
  "$HOME/.local/bin/firecracker"
```

Make sure `$HOME/.local/bin` is in `PATH`:

```bash
export PATH="$HOME/.local/bin:$PATH"
```

### 4. Install a Firecracker kernel

Rune expects the kernel at:

```text
$RUNE_FIRECRACKER_ROOT/vmlinux
```

The CI configuration uses the latest Firecracker CI Linux 6.1 kernel:

```bash
S3="https://s3.amazonaws.com/spec.ccfc.min"
ARCH="$(uname -m)"

CI_PREFIX="$(
  curl -fsSL "$S3?list-type=2&prefix=firecracker-ci/&delimiter=/" \
    | grep -oP '(?<=<Prefix>)firecracker-ci/[0-9]{8}-[^/]+/(?=</Prefix>)' \
    | sort \
    | tail -1
)"

KERNEL_KEY="$(
  curl -fsSL "$S3?list-type=2&prefix=${CI_PREFIX}${ARCH}/vmlinux-6.1" \
    | grep -oP "(?<=<Key>)${CI_PREFIX}${ARCH}/vmlinux-6\\.1\\.[0-9]+(?=</Key>)" \
    | sort -V \
    | tail -1
)"

curl -fsSL \
  -o "$RUNE_FIRECRACKER_ROOT/vmlinux" \
  "$S3/$KERNEL_KEY"
```

### 5. Build the invocation image

This is the minimal guest used to execute compiled Runes:

```bash
bash src/Rune.Firecracker/build-invocation-rootfs.sh
```

It creates:

```text
$RUNE_FIRECRACKER_ROOT/images/rune/rootfs.ext4
```

### 6. Build the compiler images

Rune uses separate isolated build images for its compiler toolchains:

```bash
bash src/Rune.Firecracker/build-rootfs.sh build rust
bash src/Rune.Firecracker/build-rootfs.sh build clang
bash src/Rune.Firecracker/build-rootfs.sh build scriptc
bash src/Rune.Firecracker/build-rootfs.sh build python
```

The profiles provide:

| Image | Languages |
| --- | --- |
| `rust` | Rust |
| `clang` | C, C++ |
| `scriptc` | JavaScript, TypeScript |
| `python` | Python |

These images only need to be rebuilt when their toolchains or build environment change.

### 7. Build the warm snapshot

The runner starts invocation microVMs from a Firecracker snapshot. Build it after the kernel and invocation image are ready:

```bash
bash src/Rune.Firecracker/build-snapshot.sh
```

This creates:

```text
$RUNE_FIRECRACKER_ROOT/snapshot/vmstate
$RUNE_FIRECRACKER_ROOT/snapshot/memory
```

Rebuild the snapshot whenever the invocation image or kernel changes.

### 8. Start Redis

Start a local Redis server:

```bash
redis-server
```

If Redis is managed by your system instead:

```bash
sudo systemctl start redis-server
```

Verify it:

```bash
redis-cli ping
```

You should receive:

```text
PONG
```

### 9. Configure Discord

Create a bot in the Discord Developer Portal and copy its bot token.

Copy the example configuration:

```bash
cp "appsettings - example.json" appsettings.json
```

Then set the token in `appsettings.json`:

```json
{
  "Discord": {
    "Token": "YOUR_BOT_TOKEN"
  }
}
```

`appsettings.json` is ignored by Git, so the token stays local.

Rune currently subscribes to:

- Guilds
- Guild Messages
- Guild Message Reactions
- Message Content

Enable the **Message Content Intent** for the application in the Discord Developer Portal.

Invite the application to the server where you want to test Rune and include the application-commands scope.

### 10. Start Rune

Rune consists of two long-running processes. Both must use the same Redis instance and Firecracker root.

Start the bot:

```bash
dotnet run --project src/Rune.Bot
```

In another terminal, with the same environment variables:

```bash
cargo run --manifest-path src/Rune.Firecracker.Runner/Cargo.toml
```

Keep these variables available in both shells:

```bash
export RUNE_REDIS_URL="redis://127.0.0.1:6379/"
export RUNE_FIRECRACKER_ROOT="$HOME/.local/share/rune/firecracker"
```


Once both processes are running and the bot is online in Discord, Rune is ready for local testing.

## Status

Rune is under active development.
