# Gateway event runes

## Objective

Replace the MessageCreate-only spike with a working event-rune system covering
four NetCord gateway events:

- `MessageCreate`;
- `MessageDelete`;
- `MessageReactionAdd`; and
- `MessageReactionRemove`.

This work must establish how an event rune is registered, compiled, stored,
validated and dispatched. It must not implement these events as four unrelated
special cases.

## Rune.API rule

Rune.API subsets the public API of NetCord 1.0.0-beta.16. It may omit NetCord
types and members. It must not add Discord-facing types or members, rename them
in the canonical contract, or change their behaviour.

Language bindings may translate canonical names into the established idiom of
the target language. For example, JavaScript may expose `ChannelId` as
`channelId` and Python may expose it as `channel_id`. These are projections of
the same canonical member.

Private transport functions used by the native ABI are implementation details.
Generated Rune.API bindings must not present them as invented NetCord members.

## Registration model

One registered rune handles exactly one gateway event. A rune has one source
file, one compiled Component and one `RuneEventType`.

The registration command requires the event alongside the existing name and
file:

```text
/rune register name:<name> event:<event> file:<file>
```

The selectable event values use the NetCord event names:

```text
MessageCreate
MessageDelete
MessageReactionAdd
MessageReactionRemove
```

Registration passes the selected `RuneEventType` into the language compiler.
The compiler uses it to select the corresponding WIT world and generate the
language wrapper. Before the rune enters the registry, the compiled Component
must be validated against that world.

An update recompiles the new source for the rune's existing event type. Updating
source does not change the registered event. A rune must be removed and
registered again to change its event.

Registration and update are transactional:

- failed compilation or Component validation does not register a rune;
- a failed update leaves the existing compiled rune active; and
- a successful update replaces only that rune and preserves its event type.

The existing list and info commands continue to display the event type.

## Dispatch model

The bot installs one gateway handler for each supported NetCord event. A
handler converts the NetCord event payload into the corresponding Rune.API
projection and dispatches it with the matching `RuneEventType`.

The registry selects only enabled runes whose guild ID and event type both
match the invocation. A rune registered for one event must never be invoked for
another event.

Runes remain scoped to guilds. Events without a `GuildId` are ignored in this
slice, consistently with the current MessageCreate behaviour.

Failures from delete and reaction runes must not create Discord messages. They
are reported through the runtime's existing diagnostic path. MessageCreate's
current user-facing failure behaviour is preserved.

## Canonical Component worlds

The WIT package defines shared NetCord-subset types and four worlds. Every world
exports one function named `handle`; its parameter is the actual NetCord event
payload type.

| World | `handle` parameter |
| --- | --- |
| `message-create-rune` | `Message` |
| `message-delete-rune` | `MessageDeleteEventArgs` |
| `message-reaction-add-rune` | `MessageReactionAddEventArgs` |
| `message-reaction-remove-rune` | `MessageReactionRemoveEventArgs` |

No Rune-specific event envelope is exposed to rune authors.

## Selected NetCord members

### `Message`

NetCord type: `NetCord.Gateway.Message`.

| Member | Canonical type |
| --- | --- |
| `Id` | `u64` |
| `ChannelId` | `u64` |
| `Content` | `string` |
| `Author` | `User` |

### `User`

NetCord type: `NetCord.User`.

| Member | Canonical type |
| --- | --- |
| `Id` | `u64` |
| `Username` | `string` |

### `MessageDeleteEventArgs`

NetCord type: `NetCord.Gateway.MessageDeleteEventArgs`.

| Member | Canonical type |
| --- | --- |
| `ChannelId` | `u64` |
| `GuildId` | `option<u64>` |
| `MessageId` | `u64` |

### `MessageReactionEmoji`

NetCord type: `NetCord.MessageReactionEmoji`.

| Member | Canonical type |
| --- | --- |
| `Animated` | `bool` |
| `Id` | `option<u64>` |
| `Name` | `option<string>` |

### `ReactionType`

NetCord type: `NetCord.ReactionType`.

| Member | Canonical value |
| --- | --- |
| `Normal` | `0` |
| `Burst` | `1` |

### `MessageReactionAddEventArgs`

NetCord type: `NetCord.Gateway.MessageReactionAddEventArgs`.

| Member | Canonical type |
| --- | --- |
| `Burst` | `bool` |
| `ChannelId` | `u64` |
| `Emoji` | `MessageReactionEmoji` |
| `GuildId` | `option<u64>` |
| `MessageAuthorId` | `option<u64>` |
| `MessageId` | `u64` |
| `Type` | `ReactionType` |
| `UserId` | `u64` |

`BurstColors` and `User` are omitted in this slice. Including `User` would
require a faithful `GuildUser` subset rather than changing its type to `User`.

### `MessageReactionRemoveEventArgs`

NetCord type: `NetCord.Gateway.MessageReactionRemoveEventArgs`.

| Member | Canonical type |
| --- | --- |
| `Burst` | `bool` |
| `ChannelId` | `u64` |
| `Emoji` | `MessageReactionEmoji` |
| `GuildId` | `option<u64>` |
| `MessageId` | `u64` |
| `Type` | `ReactionType` |
| `UserId` | `u64` |

Discord snowflakes retain their unsigned 64-bit values in the canonical
contract. Language bindings must use lossless representations.

## Reply boundary

The current spike import, `reply(content: string)`, is not a Rune.API member and
must not be presented as one by generated bindings.

NetCord provides
`RestMessage.ReplyAsync(ReplyMessageProperties, RestRequestProperties?,
CancellationToken)`, returning `Task<RestMessage>`. A public Rune.API reply
operation must preserve that observable contract. The current buffered, void
reply action does not do so.

This specification therefore covers event registration and event-input
projection. A faithful `ReplyAsync` projection requires a later executable
specification for completing the asynchronous host call and returning the
resulting `RestMessage`.

## Delivery slices

Each slice starts with the smallest meaningful automated test, confirmed RED
for the expected reason. Production code is then added until the focused and
complete suites are GREEN.

### Slice 1: event-aware registration

Executable specifications must prove that:

- registration requires and stores one supported `RuneEventType`;
- the selected event is passed to compilation;
- a Component built for a different event is rejected before registration;
- update compilation uses and preserves the existing event;
- failed registration and update leave the registry unchanged; and
- registry selection returns only enabled runes matching both guild and event.

### Slice 2: typed Component invocation

Add one native integration specification per event. Each uses a real Rust Wasm
Component generated from the canonical WIT and proves that every selected
member crosses the public C# API, native C ABI and Component Model boundaries.

Use distinct non-zero ID values and cover nullable fields in both their present
and absent states. Reaction tests cover standard emoji, custom emoji and both
`ReactionType` values. IDs must never pass through a floating-point
representation.

### Slice 3: NetCord gateway wiring

Executable specifications must prove that:

- each NetCord gateway handler creates the correct invocation type;
- each handler preserves every selected member;
- guild-less events are ignored;
- one gateway event invokes only runes registered for that event; and
- failures from delete and reaction runes do not send messages to Discord.

## Overall acceptance criteria

- All four event types can be selected during `/rune register`.
- A rune is compiled and validated for its selected event.
- All four event payloads use strict selected subsets of their NetCord types.
- The bot dispatches all four events to the correct enabled guild runes.
- The four WIT worlds share common type definitions.
- No Rune-specific Discord type or member is exposed.
- Existing execution limits, transactional action handling and diagnostics
  remain green.
- The complete required repository checks pass.

## Outside this specification

- `MessageDeleteBulk`;
- `MessageReactionRemoveAll`;
- `MessageReactionRemoveEmoji`;
- application commands and other interactions;
- multiple event handlers in one rune;
- changing a rune's event through `/rune update`; and
- language binding and documentation generation beyond the wrapper needed by
  the integration fixtures.

## NetCord references

- <https://netcord.dev/docs/NetCord.Gateway.GatewayClient.html>
- <https://netcord.dev/docs/NetCord.Gateway.Message.html>
- <https://netcord.dev/docs/NetCord.Gateway.MessageDeleteEventArgs.html>
- <https://netcord.dev/docs/NetCord.Gateway.MessageReactionAddEventArgs.html>
- <https://netcord.dev/docs/NetCord.Gateway.MessageReactionRemoveEventArgs.html>
- <https://netcord.dev/docs/NetCord.MessageReactionEmoji.html>
- <https://netcord.dev/docs/NetCord.ReactionType.html>
