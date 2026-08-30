# Rune.API over Firecracker microVMs

## Status

This specification is stacked on `experiment/firecracker-multilang-pools` and replaces the remaining Component/WIT assumptions in the Rune.API generation path with a microVM RPC model.

## Principle

Rune.API is a deliberately selected projection of NetCord, not a second Discord SDK.

The canonical `api/rune-api.yaml` file is the single source of truth for:

- which NetCord gateway payloads a rune may receive;
- which object properties are copied into immutable invocation snapshots;
- which inheritance relationships are preserved by language SDKs;
- which NetCord instance methods may cross the microVM boundary;
- the private RPC call identifiers used by the guest runtime and host;
- generated wrappers for every supported rune language; and
- generated API reference documentation.

A member or method cannot be added merely by writing it into a wrapper. The generator validates the selection against the pinned NetCord assembly before producing any artefact.

## Initial event surface

Only the four gateway events already supported by Rune are part of this slice:

| Rune event | NetCord source | Rune payload |
| --- | --- | --- |
| `MessageCreate` | `GatewayClient.MessageCreate` | `Message` |
| `MessageDelete` | `GatewayClient.MessageDelete` | `MessageDeleteEventArgs` |
| `MessageReactionAdd` | `GatewayClient.MessageReactionAdd` | `MessageReactionAddEventArgs` |
| `MessageReactionRemove` | `GatewayClient.MessageReactionRemove` | `MessageReactionRemoveEventArgs` |

The payload delivered to the microVM is a snapshot. Reading a property never causes a Discord request and never reaches back into NetCord.

## Selected object model

`Message` is projected from `NetCord.Gateway.Message` and preserves the selected inheritance from `NetCord.Rest.RestMessage`.

The first selected `RestMessage` snapshot contains:

- `Id`;
- `ChannelId`;
- `Content`; and
- `Author`, projected to the selected `User` snapshot.

The delete and reaction event argument types expose only their explicitly selected scalar and nested snapshot properties from the manifest. They do not expose the gateway client, cache, channel objects, guild objects or arbitrary REST resources.

`ReplyMessageProperties` initially contains only `Content`.

## Initial callable method

The first and only host call in this slice is the selected one-argument form of:

```csharp
NetCord.Rest.RestMessage.ReplyAsync(ReplyMessageProperties replyMessage)
```

NetCord also exposes optional request properties and a cancellation token on the canonical method. Rune.API deliberately omits those parameters for now. The host supplies its own invocation cancellation token.

Because gateway `Message` derives from `RestMessage`, message-create runes inherit the same reply operation naturally.

Language bindings translate the asynchronous naming idiom while preserving the canonical identity:

| Language | Rune call |
| --- | --- |
| JavaScript | `await message.reply(...)` |
| TypeScript | `await message.reply(...)` |
| Python | `await message.reply(...)` |
| Ruby | `message.reply(...)` through the guest async runtime |
| Rust | `message.reply(...)` through the generated runtime binding |
| C | `rune_message_reply(...)` / inherited `RestMessage` ABI surface |
| C++ | `message.reply(...)` |
| C# | `await message.ReplyAsync(...)` |

The RPC identifier remains `NetCord.Rest.RestMessage.ReplyAsync` in every language.

## Explicitly out of scope

This initial API does not expose:

- fetching a message, channel, member or guild;
- looking up another channel;
- guild or member management;
- arbitrary REST requests;
- the NetCord gateway client;
- NetCord caches;
- application commands or interactions;
- permissions administration;
- arbitrary NetCord object traversal; or
- a generic escape hatch from the selected API into NetCord.

Those capabilities must be added later as explicit Rune.API selections with their own policy and host-call semantics.

## Generation model

The generator produces two classes of data.

Snapshot artefacts describe values that are serialized once into the invocation envelope. They are ordinary data inside the VM and require no host round trip when read.

Callable artefacts describe selected NetCord instance methods. Language wrappers turn those methods into a private RPC request containing:

- the canonical method identifier;
- an opaque receiver identity owned by the invocation;
- the selected arguments; and
- a correlation identifier supplied by the guest runtime.

The microVM runtime sends the request over the Rune host channel. The host owns the corresponding live NetCord object, executes the selected method, projects the result back into a Rune.API snapshot and returns it to the same invocation.

The opaque receiver and transport fields are implementation metadata. They are not Rune.API properties and are omitted from generated public documentation.

## Transport boundary

The generated `generated/api/rpc.json` file is a machine-readable list of the host calls selected by the Rune.API manifest. It is not an author-facing API definition and it does not replace `rune-api.yaml`.

The intended Firecracker transport is vsock. Invocation VMs do not need general network access merely to use Rune.API.

Conceptually:

```text
NetCord object
    |
Rune host adapter
    |
selected Rune RPC
    |
Firecracker vsock boundary
    |
guest runtime transport
    |
generated language wrapper
    |
rune
```

## Generated languages

The same model now emits bindings and API reference material for all languages supported by the multi-language Firecracker experiment:

- JavaScript;
- TypeScript;
- Python;
- Ruby;
- Rust;
- C;
- C++; and
- C#.

The JavaScript and TypeScript SDKs may share the same ScriptC guest runtime implementation, but they remain separate generated language surfaces because their type information and author-facing source are different.

## Build and CI

CI runs the generator before the managed formatting, build and test steps. Generator tests validate that:

- the four event mappings resolve against NetCord 1.0.0-beta.16;
- selected members and methods exist on the pinned NetCord types;
- `Message` really inherits the selected `RestMessage` projection;
- `ReplyAsync` resolves to the canonical NetCord method;
- every supported language is emitted from the same model;
- all bindings carry the same canonical host-call identity;
- the RPC manifest contains no unselected fetch/guild/client operations; and
- generation is deterministic.

## Next implementation slice

This PR establishes the API model, generation and RPC contract. The next runtime slice should connect the generated transport hooks to the Firecracker guest protocol and implement the host dispatcher for `RestMessage.ReplyAsync` while retaining the live receiver object only for the lifetime of the invocation.
