# RestMessage.ReplyAsync rune projection

## Status

Planning draft for the next implementation slice. This document is not an
implementation and must remain uncommitted until it has been reviewed.

## Objective

Replace the buffered `reply(content: string)` spike with the first callable
Rune.API method: the selected projection of
`NetCord.Rest.RestMessage.ReplyAsync`.

The rune must observe the same essential operation as a NetCord caller:

- it supplies `ReplyMessageProperties`;
- it waits for the Discord REST request to complete;
- it receives the created `RestMessage`; and
- a failed REST request fails the awaited call instead of being reported only
  after `handle` returns.

This slice establishes the reusable asynchronous boundary between a Wasm
Component running in the Rust native library and NetCord running in the C# bot.
It must not move Discord REST ownership into Rust or communicate through
standard input and output.

## Rune.API rule

Rune.API remains a selected subset of the public API of NetCord
1.0.0-beta.16. It may omit types, members and optional call forms. It must not
invent Discord-facing types or members, rename canonical members, or change the
observable meaning of an included operation.

The canonical NetCord declaration is:

```csharp
Task<RestMessage> ReplyAsync(
    ReplyMessageProperties replyMessage,
    RestRequestProperties? properties = null,
    CancellationToken cancellationToken = default)
```

This slice supports the valid one-argument call form
`ReplyAsync(replyMessage)`. `RestRequestProperties` is omitted from Rune.API
for now. The host supplies its invocation cancellation token when it calls
NetCord; that token is runtime control, not a new Rune.API parameter.

Language bindings translate names and asynchronous syntax into the target
language's established idiom. In particular, NetCord's `Async` suffix is a C#
naming convention rather than a separate operation, so it is omitted in
languages that conventionally express asynchronous behaviour through `await`.
Bindings must still identify the declaring NetCord type, canonical method,
parameter and result types in generated documentation.

| Language | Projected call | Canonical member |
| --- | --- | --- |
| C# | `ReplyAsync(...)` | `RestMessage.ReplyAsync` |
| JavaScript | `reply(...)` | `RestMessage.ReplyAsync` |
| Python | `reply(...)` | `RestMessage.ReplyAsync` |
| Rust | `reply(...)` | `RestMessage.ReplyAsync` |

## Selected NetCord surface

### `Message`

`NetCord.Gateway.Message` continues to project the members selected by the
gateway-event specification. Because NetCord `Message` derives from
`RestMessage`, a message-create rune also receives the selected
`RestMessage.ReplyAsync` method.

### `RestMessage`

NetCord type: `NetCord.Rest.RestMessage`.

The result of `ReplyAsync` is a snapshot with these selected members:

| Member | Canonical type |
| --- | --- |
| `Id` | `u64` |
| `ChannelId` | `u64` |
| `Content` | `string` |
| `Author` | `User` |

It also exposes the same selected `ReplyAsync` method. This follows NetCord:
the method is declared on `RestMessage`, not only on gateway `Message`.

### `ReplyMessageProperties`

NetCord type: `NetCord.Rest.ReplyMessageProperties`.

| Member | Canonical type |
| --- | --- |
| `Content` | `option<string>` |

All other properties and helper methods are omitted. The existing rune reply
length limit applies when `Content` is present. `null` and the empty string
must remain distinct values at the Rune.API boundary and must be passed to
NetCord unchanged.

### `User`

The existing selected `NetCord.User` projection is reused:

| Member | Canonical type |
| --- | --- |
| `Id` | `u64` |
| `Username` | `string` |

## Author-facing shape

The following examples show the same canonical operation translated for each
language. Exact wrapper syntax is owned by the language binding generator.

```javascript
export async function handle(message) {
  const reply = await message.reply({ content: "Hello" });
  await reply.reply({ content: `Created ${reply.id}` });
}
```

```python
async def handle(message):
    reply = await message.reply(ReplyMessageProperties(content="Hello"))
    await reply.reply(
        ReplyMessageProperties(content=f"Created {reply.id}")
    )
```

The wrappers may hide transport handles and lowering functions. They must not
present those implementation details as Rune.API members.

## Execution model

`ReplyAsync` is an awaited host call, not a buffered action.

1. The rune calls `ReplyAsync` on a projected `Message` or `RestMessage`.
2. The Rust runtime emits one typed host-call request and suspends that
   invocation.
3. The C# runtime awaits the matching NetCord `ReplyAsync` call.
4. On success, C# maps the returned NetCord `RestMessage` to the selected
   snapshot and completes the host call.
5. Rust resumes the same Component invocation with that result.
6. On failure, the awaited call fails in the rune and, if uncaught, the rune is
   reported through the existing diagnostic path.

The initial receiver must map to the actual NetCord `Message` that caused the
dispatch. A `RestMessage` returned by NetCord must remain associated with that
actual returned object for later selected method calls. The runtime must not
refetch a message merely to reconstruct a receiver.

An invocation may issue several host calls sequentially. Each response is
correlated to exactly one invocation and request. Completion for an unknown,
completed or cancelled request is rejected and cannot resume another rune.

## Native-library boundary

The C ABI uses an opaque invocation handle and owned buffers. Its state machine
has these observable states:

| State | Meaning |
| --- | --- |
| `Running` | Rust is executing or resuming the Component. |
| `AwaitingHostCall` | C# must execute one returned host-call request. |
| `Completed` | `handle` returned successfully. |
| `Failed` | execution or an uncaught host-call failure ended the invocation. |
| `Cancelled` | the caller cancelled or timed out the invocation. |

The ABI must provide operations equivalent to start, poll, complete host call,
fail host call, cancel and destroy. Exact exported names are an implementation
choice, but every allocation must have one documented owner and release path.

The protocol remains an in-process native-library API. JSON may remain an
internal payload encoding during this slice, but it is not the Rune.API schema
and must preserve unsigned 64-bit values exactly.

Rust owns Component execution and Wasmtime state. C# owns NetCord objects and
REST calls. The design must not use reverse P/Invoke to block a Rust thread on a
managed `Task`; C# drives the opaque invocation until it completes.

Wasmtime's asynchronous Component support may be used to suspend the guest
while a host future is pending. Before production changes, an executable
feasibility test must prove that the Wasmtime version resolved in `Cargo.lock`
and the chosen guest toolchain can suspend and resume the selected Component
call. If that test cannot be made green without changing the public projection,
this specification must be revised rather than silently falling back to
buffered actions.

## Failure, cancellation and limits

- The existing wall-clock execution deadline includes time spent awaiting
  NetCord.
- Fuel is charged only while guest Wasm executes; awaiting the host does not
  replenish fuel.
- Cancelling or timing out an invocation cancels the in-flight NetCord call and
  prevents later resumption.
- Per-invocation host-call and output-size limits remain enforced across every
  suspension and resumption.
- A malformed host response fails only its invocation and does not poison the
  loaded Component or native runtime.
- A NetCord exception crosses the private transport as a host-call failure. It
  is surfaced using the target language's normal rejected-await or exception
  behaviour; Rune.API does not invent a Discord error type.

The old action queue delayed all Discord effects until `handle` completed. That
transactional behaviour is incompatible with an awaited method that returns a
real `RestMessage`. After NetCord successfully creates a reply, a later rune
failure cannot roll it back. Tests and documentation must state this explicitly
rather than implying that the whole handler is atomic.

## Test-driven delivery plan

Each delivery step begins with the smallest meaningful automated test, observed
RED for the expected reason. Production code follows only until that test and
the relevant suites are GREEN.

### Slice 1: asynchronous native round trip

Use a test-only host operation, outside Rune.API, to prove that:

- a Component invocation can suspend without blocking the managed caller;
- C# receives a request through the native ABI and completes it asynchronously;
- the guest resumes with the supplied result;
- two sequential requests preserve order and correlation;
- failure and cancellation resume or end only the correct invocation; and
- all native request, response and invocation buffers are released.

This slice changes transport only. It must not expose the test operation in
language wrappers or generated Rune.API documentation.

### Slice 2: exact `ReplyAsync` projection

Add `ReplyMessageProperties`, `RestMessage` and the selected `ReplyAsync` call
to the canonical API metadata, WIT lowering and generated language wrappers.

Native integration fixtures must prove that:

- all selected request and result members cross C#, the C ABI and the Component
  boundary;
- IDs retain their full unsigned 64-bit values;
- `Content = null`, `Content = ""` and non-empty content remain distinct;
- the returned `RestMessage` can be the receiver of a second `ReplyAsync`;
- an awaited host failure can be caught by the rune; and
- an uncaught host failure fails the invocation with no fabricated result.

### Slice 3: NetCord adapter

Use a fake NetCord-facing adapter for deterministic tests. Prove that:

- the initial receiver is the actual gateway `Message` supplied by the bot;
- `ReplyMessageProperties.Content` is mapped without semantic changes;
- the adapter awaits NetCord and returns the actual result's selected snapshot;
- a second call uses the returned NetCord `RestMessage` as its receiver;
- NetCord failure and cancellation propagate through the suspended invocation;
  and
- receiver state is removed after success, failure, cancellation and timeout.

The existing `message.reply` buffered request and native reply action are then
removed. No compatibility alias is retained.

## Overall acceptance criteria

- Message-create runes can await the selected NetCord `ReplyAsync` operation.
- The call accepts `ReplyMessageProperties` and returns the selected
  `RestMessage`, rather than accepting a bare string or returning `void`.
- The initial and returned receivers correspond to actual NetCord objects.
- NetCord success, failure and cancellation are observable at the rune call
  site.
- Rust remains the Component executor and C# remains the NetCord host.
- The boundary is a native library API and uses neither stdin nor stdout.
- No private transport function or handle appears as a Rune.API member.
- Existing event registration and dispatch behaviour remains green.
- The complete required repository checks pass.

## Outside this specification

- `RestRequestProperties`;
- author-supplied cancellation tokens;
- the remaining `ReplyMessageProperties` members;
- other `RestMessage` methods, including reaction methods;
- application commands and interactions;
- permissions and policy for Rune.API calls;
- concurrent host calls from one invocation;
- API documentation generation beyond the wrappers needed by integration
  fixtures; and
- physical Discord verification, which remains a manual integration gate.

## References

- <https://netcord.dev/docs/NetCord.Gateway.Message.html>
- <https://netcord.dev/docs/NetCord.Rest.RestMessage.html>
- <https://netcord.dev/docs/NetCord.Rest.ReplyMessageProperties.html>
- <https://docs.rs/wasmtime/47.0.4/wasmtime/struct.Config.html>
- <https://docs.wasmtime.dev/api/wasmtime/component/struct.Func.html>
