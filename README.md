<p align="center">
  <img src="assets/rune.png" alt="Rune" width="160">
</p>

<h1 align="center">Rune</h1>

<p align="center">
  A small, sandboxed scripting platform for Discord.
</p>

Rune lets you upload scripts directly through Discord and run them as isolated WebAssembly Components.

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

Install the Component toolchains:

```sh
npm install --global @bytecodealliance/jco@1.32.1
python3 -m pip install componentize-py==0.25.0
rustup target add wasm32-wasip2
```

Rune also requires .NET 10 and Cargo. Start it from the repository root:

```sh
dotnet run --project src/Rune.Bot
```

## Status

Rune is under active development. JavaScript, Python and Rust event runes compile to Components and execute through the embedded Rust/Wasmtime runtime.

The files in `examples/` are native-runtime smoke tests. Register one for
`MessageCreate`, then send `!native-test`; a successful invocation is silent,
while a failed projection is reported by the bot. Host-backed NetCord methods
such as `message.reply(...)` are the next implementation slice.
