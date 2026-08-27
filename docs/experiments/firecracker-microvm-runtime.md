# Disposable Firecracker microVM runtime experiment

Status: experimental. This is the follow-up to
`shared-javascript-interpreter.md` and deliberately abandons WebAssembly,
Wasmtime and the in-process QuickJS reference backend for execution.

## Question

Can Rune make the isolation boundary language-independent by executing every
invocation in a disposable Firecracker microVM, while keeping Discord latency
low enough through pre-restored snapshot pools?

The experiment keeps NetCord and the existing Rune.API/event projection in the
bot. It replaces compilation and in-process execution with this path:

```text
Discord Gateway
     |
   NetCord
     |
Rune.Bot
     |
     +---- XADD rune:invocations:javascript ----+
     +---- XADD rune:invocations:python --------+--> Redis Streams
     +---- XADD rune:invocations:rust ----------+
                                                   |
                                      Firecracker runner(s)
                                                   |
                           per-language warm restored VM pool
                                                   |
                                      acquire exactly one VM
                                                   |
                                      vsock invocation
                                                   |
                                       execute one rune
                                                   |
                                      XADD rune:results
                                                   |
                                      destroy the VM
                                                   |
                                 restore a clean replacement
                                                   |
Rune.Bot <------------- Redis result consumer ----+
     |
NetCord host action, for example message.reply(...)
```

## What changed

Registration no longer runs `jco`, `componentize-py`, Cargo-to-Wasm or any
other WebAssembly compiler. Rune stores the uploaded source and event type.
Invalid source can therefore register successfully and fail only when invoked;
that is an intentional prototype trade-off.

`RuneEventDispatcher` no longer executes untrusted code. For every enabled rune
matching the guild and gateway event it writes one invocation envelope to the
language-specific Redis stream. Discord snowflakes are encoded as decimal
strings in that transport envelope so JavaScript can reconstruct `BigInt` and
Python/Rust can reconstruct lossless integers.

The bot consumes `rune:results` in a separate background service. A result is
converted back into the authoritative typed invocation before any NetCord host
action is applied. The microVM cannot choose a different Discord guild,
channel or original message for `message.reply(...)`.

## Warm VM model

There are independent JavaScript, Python and Rust pools. A pool contains
Firecracker processes which have already loaded and resumed a snapshot. These
are genuinely available VMs, not merely snapshot files waiting to be restored.

A snapshot is created only after the guest agent has started its language
worker and prints `RUNE_READY`:

- JavaScript has Node and `node:vm` loaded and warmed;
- Python has the interpreter and worker modules loaded and warmed;
- Rust has Python orchestration loaded and has already run a dummy `rustc`
  compile to warm compiler pages and metadata.

The guest has a vsock listener on port 5000. Restored clones use Firecracker's
`vsock_override` so every host-side VM receives a unique Unix socket path.
There is no configured network interface in this prototype.

Invocation lifecycle is strictly:

```text
clean restored VM -> acquire -> one invocation -> result -> kill -> delete runtime dir
                                                        |
                                                        +-> restore fresh clone
```

A used VM never returns to the pool.

## Backlog-driven pool sizing

Each language pool has a minimum and maximum size. The runner periodically
checks the Redis stream length and computes:

```text
target = clamp(ceil(backlog / backlog_per_vm), min_vms, max_vms)
```

Defaults are:

```text
RUNE_VM_MIN=1
RUNE_VM_MAX=8
RUNE_VM_BACKLOG_PER_VM=4
RUNE_VM_AUTOSCALE_INTERVAL_MS=250
```

Invocation entries remain in the stream until execution has finished and the
result has been committed, so `XLEN` includes queued and in-flight work. When a
VM finishes, the runner destroys it, decrements the in-flight count and the pool
maintainer restores a fresh VM if the current total is below the target.

This is deliberately local autoscaling of microVM capacity. Scaling the number
of runner hosts is a separate problem.

## Security model

The language runtime is no longer the security boundary. `node:vm` is used to
present a convenient JavaScript global object, not to protect the host. Python
code has its normal interpreter powers and Rust code is a normal native Linux
binary inside the guest. That is acceptable only because the entire guest is
disposable.

The prototype currently provides these outer controls:

- one Firecracker microVM per invocation;
- fixed vCPU and guest memory from the snapshot;
- read-only root filesystem;
- writable ephemeral `/tmp` backed by guest tmpfs;
- no Firecracker network interface;
- bounded source, request, response, action and reply sizes;
- a host-side wall-clock invocation timeout which kills the Firecracker
  process; and
- no reuse of a VM after untrusted code has run.

This is **not production hardening yet**. Before treating the runner as an
internet-facing multi-tenant service, add Firecracker `jailer`, host cgroups or
systemd resource controls, runner privilege separation, Redis authentication
and ACLs, pending-entry reclamation (`XAUTOCLAIM`), snapshot provenance/version
checks and explicit observability.

Snapshot clones also deserve special attention for randomness. A snapshot can
clone guest entropy and userspace PRNG state. A production image should use a
kernel/Firecracker combination with VM generation ID handling and should verify
post-restore reseeding rather than assuming cloned VMs have independent random
streams.

## Files

```text
native/Rune.Firecracker.Runner/   Redis consumer, autoscaler, pools, snapshot restore
native/Rune.Firecracker.Guest/    PID 1 guest agent and AF_VSOCK bridge
firecracker/guest/                language workers
firecracker/images/               language rootfs Docker build definitions
firecracker/build-rootfs.sh       Docker image -> read-only ext4 rootfs
firecracker/build-snapshot.sh     boot, warm, pause and snapshot one language
firecracker/build-all.sh          build all three images and snapshots
firecracker/check-host.sh         Linux/KVM prerequisite check
```

## Linux/KVM setup

Firecracker itself must run on Linux with KVM. Rune.Bot and Redis do not have
that restriction, so a development Mac can run the Discord bot while a separate
Linux machine consumes the streams.

On the Linux runner install Firecracker, Docker, `e2fsprogs`, `curl`, Python 3
and Redis (or point the runner at an existing Redis). Put a Firecracker-compatible
uncompressed Linux kernel at:

```text
/var/lib/rune/firecracker/vmlinux
```

or set `RUNE_KERNEL` to its absolute path. The kernel must include the drivers
required by Firecracker virtio block, serial console, devtmpfs and AF_VSOCK.

Prepare a writable state directory and verify KVM:

```sh
sudo mkdir -p /var/lib/rune/firecracker
sudo chown -R "$USER" /var/lib/rune/firecracker
./firecracker/check-host.sh
```

Build the three root filesystems and warm snapshots:

```sh
./firecracker/build-all.sh
```

Do not move the rootfs files after snapshot creation. Firecracker snapshots
refer to external block devices by host path. Rebuild the snapshot when the
rootfs path, guest image, kernel or incompatible Firecracker version changes.

## Run the prototype

Start Redis somewhere reachable by both processes. For a single Linux test
machine, for example:

```sh
docker run --rm --name rune-redis -p 6379:6379 redis:8-alpine
```

Start the Firecracker runner:

```sh
RUNE_REDIS_URL=redis://127.0.0.1:6379/ \
  cargo run --release -p rune-firecracker-runner
```

Start Rune.Bot exactly as before, except point it at Redis:

```sh
RUNE_REDIS=127.0.0.1:6379 \
  dotnet run --project src/Rune.Bot
```

If the bot runs on macOS and the runner runs on Linux, use the Redis server's
private-network address in both variables. Do not expose an unauthenticated
Redis port to the public internet.

## Discord smoke test

Upload one of:

```text
examples/firecracker-rune.js
examples/firecracker-rune.py
examples/firecracker-rune.rs
```

using the existing command:

```text
/rune register name:firecracker-test event:MessageCreate file:<file>
```

Then send:

```text
!firecracker
```

The expected path is Discord -> NetCord -> Redis -> warm microVM -> vsock ->
language worker -> Redis result -> NetCord reply. The VM which handled that
message must disappear and a clean snapshot clone must replace it.

The Rust worker currently implements the `MessageCreate` prototype surface;
JavaScript and Python accept all four existing gateway payloads, while the only
host action currently implemented by Rune.Bot remains `message.reply` on
`MessageCreate`.

## Measurements to collect

The experiment should be judged on measured boundaries rather than theoretical
Firecracker startup claims. Record at least:

- Discord event to Redis enqueue;
- queue wait time;
- warm-pool hit rate;
- snapshot restore time while replenishing a pool;
- vsock request/response time;
- language execution time;
- end-to-end event-to-reply p50/p95/p99;
- idle and peak memory per language pool;
- pool refill time after an invocation; and
- backlog versus target/actual VM count during a burst.

The key question is whether keeping *restored* clean VMs available makes the
security boundary cheap enough that Rune no longer needs language-specific
WebAssembly sandbox engineering.
