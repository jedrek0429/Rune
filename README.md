<p align="center">
  <img src="assets/rune.png" alt="Rune" width="160">
</p>

<h1 align="center">Rune</h1>

<p align="center">
  A small, sandboxed scripting platform for Discord.
</p>

Rune lets you upload scripts directly through Discord and run them as isolated WebAssembly modules.

Runes are written in languages such as JavaScript and Python, while Rune provides a common Discord-facing API.

The goal is to support as many languages as possible.

## Discord API

Rune.API is a strict subset of
[NetCord](https://github.com/NetCordDev/NetCord), projected into each supported
language. It can omit NetCord members but does not define a separate Discord
object model.

An event rune is registered for one of these NetCord gateway events:

- `MessageCreate`;
- `MessageDelete`;
- `MessageReactionAdd`; or
- `MessageReactionRemove`.

For example, a `MessageCreate` rune receives a selected subset of NetCord's
`Message` and `User` types.

JavaScript:

```javascript
message.id
message.channelId
message.content

message.author.id
message.author.username
```

Python:

```python
message.id
message.channel_id
message.content

message.author.id
message.author.username
```

Rust:

```rust
fn rune(message: Message) -> FnResult<()> {
    message.id;
    message.channel_id;
    message.content;

    message.author.id;
    message.author.username;

    Ok(())
}
```

Delete and reaction runes receive `MessageDeleteEventArgs`,
`MessageReactionAddEventArgs` or `MessageReactionRemoveEventArgs` respectively.

## Running locally

Rune currently requires the Extism JavaScript and Python compilers:

```sh
extism-js
extism-py
```

Once they are installed, start Rune with:

```sh
dotnet run --project src/Rune.Bot
```

## Status

Rune is under active development. The current implementation supports JavaScript and Python event runes compiled to WebAssembly.
