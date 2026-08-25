# Rune.API generation and Component runtime cutover

## Status

Planning specification. This document settles the architecture required before
the runtime cutover. It does not authorise implementation shortcuts that leave
the API duplicated in language compilers.

## Objective

Make Rune.API the single maintained selection of NetCord public contracts, use
that selection to generate every transport and language projection, and make
the Rust native library the only Wasm Component executor used by the bot.

The first coherent implementation checkpoint is:

- JavaScript, Python and Rust event runes compile to Components for any of the
  four selected gateway events;
- registration validates and stores the Component for its selected event;
- `RuneEventDispatcher` executes the registered Component through the Rust
  native library;
- more than one registered rune can be loaded and dispatched correctly; and
- the Extism execution path and its handwritten API wrappers are gone.

This checkpoint proves the real registration-to-execution path. The following
`RestMessage.ReplyAsync` slice supplies the first author-visible Discord action
for physical laptop testing.

## Governing rules

Rune.API is a strict selected subset of the public API of the pinned NetCord
version. It may omit types, members, overloads and optional call forms. It must
not invent or rename canonical Discord-facing types or members, change their
meaning, or imply that NetCord supplied data it did not supply.

Language projections may translate the selected contract into established
language conventions. For example, `ChannelId` becomes `channelId` in
JavaScript and `channel_id` in Python and Rust. An asynchronous NetCord method
such as `ReplyAsync` may become `reply` in languages where `await` already
expresses that behaviour. Every projection retains the canonical NetCord
identity in generated metadata and documentation.

Private records, resource handles, lowering functions and host imports needed
to cross the Component boundary are transport details. They must never appear
as invented Rune.API members in an author-facing binding or API reference.

## Decision 1: canonical Rune.API source format

The only hand-maintained API selection is `api/rune-api.yaml`.

It is a declarative selection manifest, not a second implementation of
NetCord. It records:

- the Rune.API schema and package version;
- the exact NetCord NuGet package and version;
- selected NetCord types;
- selected members and overloads;
- any required portable representation, such as lossless Discord snowflakes;
  and
- selected gateway events and their payload types.

It does not repeat property types, enum values, declaring types, return types
or documentation when those can be discovered from NetCord metadata. The
generator resolves them from the pinned NetCord assembly and rejects a
selection that does not match it.

The initial file has this shape:

```yaml
schema: 1

api:
  package: rune:api
  version: 0.1.0

netcord:
  package: NetCord
  version: 1.0.0-beta.16

types:
  User:
    netcord: NetCord.User
    members:
      - name: Id
        representation: snowflake
      - name: Username

  RestMessage:
    netcord: NetCord.Rest.RestMessage
    members:
      - name: Id
        representation: snowflake
      - name: ChannelId
        representation: snowflake
      - name: Content
      - name: Author
    methods:
      - name: ReplyAsync
        overload:
          parameters:
            - NetCord.Rest.ReplyMessageProperties
            - NetCord.Rest.RestRequestProperties
            - System.Threading.CancellationToken
        expose-parameters:
          - replyMessage

  Message:
    netcord: NetCord.Gateway.Message
    base: RestMessage
    members: []

  ReplyMessageProperties:
    netcord: NetCord.Rest.ReplyMessageProperties
    members:
      - name: Content

  MessageDeleteEventArgs:
    netcord: NetCord.Gateway.MessageDeleteEventArgs
    members:
      - name: ChannelId
        representation: snowflake
      - name: GuildId
        representation: snowflake
      - name: MessageId
        representation: snowflake

  MessageReactionEmoji:
    netcord: NetCord.MessageReactionEmoji
    members:
      - name: Animated
      - name: Id
        representation: snowflake
      - name: Name

  ReactionType:
    netcord: NetCord.ReactionType
    members:
      - name: Normal
      - name: Burst

  MessageReactionAddEventArgs:
    netcord: NetCord.Gateway.MessageReactionAddEventArgs
    members:
      - name: Burst
      - name: ChannelId
        representation: snowflake
      - name: Emoji
      - name: GuildId
        representation: snowflake
      - name: MessageAuthorId
        representation: snowflake
      - name: MessageId
        representation: snowflake
      - name: Type
      - name: UserId
        representation: snowflake

  MessageReactionRemoveEventArgs:
    netcord: NetCord.Gateway.MessageReactionRemoveEventArgs
    members:
      - name: Burst
      - name: ChannelId
        representation: snowflake
      - name: Emoji
      - name: GuildId
        representation: snowflake
      - name: MessageId
        representation: snowflake
      - name: Type
      - name: UserId
        representation: snowflake

events:
  MessageCreate:
    netcord: NetCord.Gateway.GatewayClient.MessageCreate
    payload: Message

  MessageDelete:
    netcord: NetCord.Gateway.GatewayClient.MessageDelete
    payload: MessageDeleteEventArgs

  MessageReactionAdd:
    netcord: NetCord.Gateway.GatewayClient.MessageReactionAdd
    payload: MessageReactionAddEventArgs

  MessageReactionRemove:
    netcord: NetCord.Gateway.GatewayClient.MessageReactionRemove
    payload: MessageReactionRemoveEventArgs
```

The committed implementation manifest for the cutover initially excludes
`ReplyMessageProperties` and `ReplyAsync`; those entries are added by the
subsequent reply specification. They are shown here to prove that the format
can represent selected overloads, inherited members and omitted optional call
parameters without a redesign.

`api/rune-api.schema.json` defines the manifest grammar. It is a schema for the
tooling format, not another copy of the selected API.

### NetCord validation

`tools/Rune.Api.Generator` resolves the exact NuGet package declared in the
manifest and builds an immutable canonical model from assembly metadata. It
must fail when:

- a selected type, member, event or overload does not exist;
- a declared base projection is inconsistent with NetCord inheritance;
- an exposed parameter is absent from the selected overload;
- a portable representation is incompatible with the underlying .NET type;
- a selected public type refers to another type that has no supported lowering;
  or
- `Rune.Bot` references a different NetCord version.

Nullable reference metadata and nullable value types determine optionality.
Enum values come from NetCord metadata. The normalised canonical model is
ordered deterministically and receives a SHA-256 API fingerprint.

## Decision 2: generated artefacts

The generator produces all API-shaped code and reference data from the
canonical model. Generated files carry an auto-generated header and are never
edited manually.

| Output | Purpose |
| --- | --- |
| `wit/rune-api.wit` | Shared transport types, host imports and one world per event. |
| `src/Rune.Core/Api/Generated/RuneApi.g.cs` | Managed transport records, event identifiers and serialisation metadata. |
| `src/Rune.Bot/Api/Generated/NetCordRuneApi.g.cs` | NetCord-to-Rune projection functions. |
| `generated/javascript/` | JavaScript runtime wrapper and TypeScript declarations. |
| `generated/python/` | Python wrapper and type stubs. |
| `generated/rust/` | Rust guest facade over generated WIT bindings. |
| `generated/api/model.json` | Stable canonical IDs, types, members, events, projections and API fingerprint. |
| `docs/api/generated/` | Reference pages, event handler skeletons and language-specific signature snippets. |

The C# files used by the host are distinct from a future C# guest backend. A
C# rune is not advertised as supported until a C# backend can produce a
conforming Component.

The generator owns mechanical API shape only. Gateway handlers retain policy
and control flow such as ignoring bot authors and guild-less events. They call
generated projection functions instead of copying selected fields by hand.

The WIT output may contain private wire types and functions needed to lower
objects, inheritance and awaited receiver methods. Generated guest facades
hide those details and expose the selected NetCord shape. A private WIT name is
not automatically a public Rune.API name.

## Decision 3: language backend contract

An entirely new language requires one language backend. Adding a Rune.API
member does not require editing existing language backends.

The runtime-facing contract is conceptually:

```csharp
public interface IRuneLanguageBackend
{
    RuneLanguageDescriptor Descriptor { get; }

    ValueTask<CompiledComponent> CompileAsync(
        RuneCompilationRequest request,
        CancellationToken cancellationToken = default);
}
```

`RuneLanguageDescriptor` supplies a stable string ID, display name, accepted
file extensions, source-code fence and toolchain description.
`RuneCompilationRequest` supplies the source, selected event/world, generated
WIT package, generated language facade, API fingerprint and isolated working
directory. `CompiledComponent` contains Component bytes, sanitised diagnostics,
the selected world and the API fingerprint.

Each backend owns only language-specific concerns:

- toolchain discovery and pinned invocation;
- the rune entry-point convention;
- project scaffolding around the user source;
- use of the generated facade and WIT bindings;
- production of a Component for the requested world;
- idiomatic name, optional value, enum, async and error projection rules;
- sanitised compiler diagnostics; and
- rendering language-specific reference signatures and minimal examples from
  the canonical model.

Each backend consumes the complete canonical model. It must not contain a
handwritten `Message`, `User` or event-argument definition.

The initial backends use Component-aware tools:

| Language | Backend toolchain |
| --- | --- |
| JavaScript | project-local `@bytecodealliance/jco` / `componentize-js` targeting the selected WIT world |
| Python | pinned `componentize-py` targeting the selected WIT world |
| Rust | pinned `wit-bindgen` or `cargo-component` integration targeting `wasm32-wasip2` |

Tool versions are locked in repository-owned manifests. The compiler never
downloads tools while handling a Discord command.

`RuneLanguage` becomes a stable string-backed `RuneLanguageId`; the current
central enum and event/language switches are removed. The backend registry is
built from registered `IRuneLanguageBackend` implementations. Discord command
choices and upload-extension recognition come from their descriptors.

Adding a language therefore requires:

1. one backend project containing the compiler adapter and projection renderer;
2. registration of that project with dependency injection;
3. its pinned toolchain installation in development and CI; and
4. passing the shared language-conformance suite.

It does not require edits to Rune.API types, event dispatch, documentation
templates, the Rust executor or other language backends.

## Decision 4: generated-artifact and conformance verification

Generation is deterministic: stable ordering, UTF-8, LF endings, no absolute
paths and no timestamps.

`dotnet run --project tools/Rune.Api.Generator -- verify` generates into a
temporary directory and byte-compares every expected output with the committed
artefact. It fails for missing, stale or unexpected generated files.

The verification suite has four layers:

1. **Canonical validation** proves every selected contract exists in the
   pinned NetCord assembly with the expected inheritance and portable lowering.
2. **Generation verification** proves every committed WIT, binding and
   reference file matches the canonical model.
3. **Backend conformance** compiles a generated minimal rune for every
   language/event pair and inspects the resulting Component's exact world and
   imports.
4. **Boundary conformance** invokes those real Components through C# and Rust
   with distinct full-width snowflakes, present and absent option values, all
   selected enum values and nested records.

Every language backend runs the same data-driven conformance cases. A backend
cannot opt out of a selected API shape. Where a language cannot represent an
unsigned 64-bit value natively, its generated facade must provide a lossless
representation and the conformance test must use a value above JavaScript's
safe integer range.

CI runs generation verification before compilation. A pull request that edits
`api/rune-api.yaml` without regenerating all artefacts fails clearly.

## Decision 5: Component compilation pipeline

Registration and update use this single pipeline:

1. Resolve the backend from its stable language ID.
2. Resolve the event to its generated WIT world from the canonical model.
3. Create an isolated temporary build directory.
4. Materialise the user source, generated language facade and exact generated
   WIT package for the current API fingerprint.
5. Invoke the backend's pinned Component-aware toolchain.
6. Collect and sanitise diagnostics, removing the temporary path.
7. Pass the output bytes, expected event world and API fingerprint to the Rust
   native validator.
8. Return `CompiledComponent`; registration or update becomes visible only
   after every preceding step succeeds.

The output must already be a WebAssembly Component. The runtime does not accept
an Extism core module and does not adapt one during invocation.

`WasmPipeline` is replaced by a Component validation facade backed by the same
Rust/Wasmtime engine used for execution. Binaryen is removed from this path
unless a later Component-aware optimisation is introduced with equivalence
tests.

The registered model is renamed accordingly:

```text
CompiledRune.Wasm       -> CompiledComponent.Bytes
RegisteredRune.Wasm     -> RegisteredRune.Component
```

The selected event/world and API fingerprint are stored alongside the bytes.
A stale fingerprint or wrong world is rejected before execution.

Backend-produced WASI imports do not become authorised merely because a new
toolchain emits them. The existing exact import-policy tests remain a gate;
socket imports remain rejected.

## Decision 6: documentation and language switching

`generated/api/model.json` is the data source for the reference documentation.
Every type, member and event has a stable canonical ID based on its NetCord
identity, for example:

```text
NetCord.Gateway.Message.Content
NetCord.Rest.RestMessage.ReplyAsync
NetCord.Gateway.GatewayClient.MessageReactionAdd
```

For each canonical ID, the model contains:

- its NetCord declaring type and canonical signature;
- its selected portable shape;
- its declaring/inherited relationship;
- the languages currently backed by executable Components;
- each backend's projected name and rendered signature; and
- links to the generated event skeletons and minimal call examples.

The documentation language switcher selects another projection of the same
canonical ID. It does not navigate among separately maintained pages. Adding
an API member updates every language projection on regeneration. Adding a
language contributes one new projection column for every existing canonical
ID after its backend passes conformance.

Generated examples cover mechanically derivable shapes:

- one handler skeleton for every event;
- property access for every selected record;
- constructors/property bags where applicable; and
- one minimal awaited call for every selected method.

Tutorial prose and larger behavioural examples may remain handwritten. They
must reference canonical IDs or generated snippets rather than restating API
signatures manually. Documentation for a language is published only when that
language has a working backend.

## Decision 7: Extism migration and deletion

The cutover replaces the execution path; it does not preserve two runtimes.

### Retained and rewritten

| Area | Outcome |
| --- | --- |
| `Rune.Runtime` project | Remains the managed facade for compilation, native execution and lifecycle orchestration. |
| `CompilerProcessRunner` | Retained for external language toolchains. |
| compiler registry | Replaced by the string-ID language backend registry. |
| `RuneService` | Uses Component compilation/validation and native lifecycle methods. |
| `RuneEventDispatcher` | Invokes the native Rust runtime directly. |
| `NativeMethods` / `RuneNativeRuntime` | Expanded to manage many registered Components and invoke by rune ID. |
| exceptions, options and DI | Retained in simplified Component-specific form. |

### Deleted after cutover tests are green

| Area | Reason |
| --- | --- |
| `Rune.Runtime/Wasm/RuneExecutor.cs` | Extism executor replaced by Rust/Wasmtime. |
| `Rune.Runtime/Wasm/RuneWasmCache.cs` | Component storage and compiled cache move to Rust. |
| `Rune.Runtime/Wasm/RuneHostFunctions.cs` | Host imports are generated from WIT and implemented by Rust/C# bridging. |
| `Rune.Runtime/Wasm/RuneExecutionContext.cs` | Invocation state belongs to the native runtime. |
| `Rune.Runtime/Wasm/RuneExecutionResult.cs` | Native completion results replace Extism results. |
| `RuneHostRequest` and `IRuneHostRequestHandler` | Buffered spike actions are not the awaited NetCord API. |
| handwritten JS/Python/Rust API records | Generated facades replace them. |
| `Extism.runtime.all` and `Extism.Sdk` | No Extism code remains. |
| Extism compiler installation in CI and README | Backends use Component-aware toolchains. |
| legacy Binaryen/core-module pipeline | Registration consumes Components. |

The old path is removed in the same implementation series that makes the new
path green. There is no runtime feature flag or compatibility fallback.

### Native component lifecycle

The Rust runtime stores multiple validated Components in a map keyed by the
rune's UUID. Loading an update validates and compiles the replacement before
atomically swapping the map entry. Removing or disabling a rune cancels its
active invocations and removes or deactivates its entry. An invocation clones
an `Arc` to the selected compiled Component, so an update can replace the
registered version while an already-started invocation finishes safely.

The C ABI therefore supports operations equivalent to:

- load or atomically replace `(rune ID, event, fingerprint, bytes)`;
- remove or deactivate a rune ID;
- invoke `(rune ID, typed event payload)`; and
- cancel active invocations for a rune ID.

Every allocation and opaque handle has one documented owner and release path.
Native panics are caught at the ABI boundary. Unknown rune IDs, wrong events and
stale fingerprints fail without affecting other loaded Components.

## Managed runtime after cutover

```text
NetCord gateway handler
    -> generated NetCord projection
    -> RuneEventDispatcher
    -> RuneNativeRuntime (C ABI)
    -> Rust Component registry and Wasmtime engine
    -> generated event world handle(...)
```

The C# process owns NetCord objects, bot policy, registration and external
compiler orchestration. Rust owns Component validation, compiled Component
caching, Wasmtime stores, execution limits and invocation state.

## Test-driven delivery plan

Every slice begins with an executable test observed RED for the expected
reason, then proceeds only until the focused and complete suites are GREEN.

### Slice 1: canonical model and generator

Prove that:

- the manifest validates against NetCord `1.0.0-beta.16`;
- all four existing event contracts produce the current semantic WIT shape;
- invalid types, members, overloads and portable representations fail clearly;
- all outputs regenerate deterministically;
- JavaScript, Python and Rust projections come from the same model; and
- generated docs retain stable canonical IDs across language projections.

Replace the current WIT and handwritten API records with generated outputs.
Do not change bot execution in this slice.

### Slice 2: language backends produce Components

Prove for every JavaScript, Python and Rust/event pair that:

- a minimal valid rune compiles through its real backend;
- the output is a Component for the selected generated world;
- the Component accepts full canonical event data;
- invalid source returns sanitised diagnostics without the temporary path; and
- missing toolchains fail with the backend and required installation named.

Replace the Extism compilers. Registration may still be connected to a test
executor until Slice 3.

### Slice 3: multi-rune native runtime

Prove that:

- several Components for different rune IDs and events can coexist;
- invocation selects by rune ID and rejects a mismatched event;
- update swaps only the selected rune after successful validation;
- failed update preserves the previous Component;
- remove, disable and cancellation affect only the selected rune; and
- fuel, memory, timeout and import limits remain isolated per invocation.

### Slice 4: bot cutover and Extism deletion

Prove through managed integration tests that:

- registration compiles, validates and loads a Component transactionally;
- all four NetCord handlers dispatch through Rust to matching guild/event
  runes;
- update, remove, enable and disable drive the native lifecycle correctly;
- dispatcher failure reporting remains isolated per rune; and
- no production service resolves or references the Extism executor.

Delete every item listed in the deletion table, update CI and local setup, then
run the complete repository suite.

This is the semi-usable branch checkpoint and receives a Conventional Commit.
It is not pushed and no pull request is opened.

### Slice 5: first physical Discord behaviour

Implement the reviewed `RestMessage.ReplyAsync` specification on the cut-over
runtime. The first laptop test is a real JavaScript, Python or Rust
`MessageCreate` rune which awaits `message.reply(...)` and receives the created
`RestMessage` projection.

## Overall acceptance criteria

- One manifest is the only hand-maintained Rune.API selection.
- The selected API is mechanically verified against the pinned NetCord
  assembly.
- WIT, C# host projections, three guest facades and reference data regenerate
  deterministically from that manifest.
- Adding an API member requires one manifest edit and regeneration.
- Adding a language requires one backend and the shared conformance suite,
  without copying existing API types.
- All three current languages compile real Components for all four events.
- The bot dispatches registered runes only through the Rust native library.
- Multiple runes and updates are safe in one native runtime.
- Extism, its compilers, its cache and its handwritten wrappers are absent.
- Existing execution and import limits remain enforced.
- All generated, Rust and .NET checks pass.
- No branch push or pull request is created during this work.

## Outside this specification

- permissions and per-rune capability policy;
- application commands and interactions;
- multiple events handled by one rune;
- a C# guest compiler backend;
- a public package registry for third-party language backends;
- handwritten tutorial content and a deployed documentation website; and
- additional NetCord methods beyond the separately specified
  `RestMessage.ReplyAsync` slice.

## References

- <https://component-model.bytecodealliance.org/design/wit.html>
- <https://component-model.bytecodealliance.org/language-support.html>
- <https://component-model.bytecodealliance.org/language-support/building-a-simple-component/javascript.html>
- <https://github.com/bytecodealliance/componentize-py>
- <https://github.com/bytecodealliance/cargo-component>
- <https://netcord.dev/docs/NetCord.Gateway.GatewayClient.html>
