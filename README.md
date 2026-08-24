<p align="center">
  <img src="assets/rune.png" alt="Rune" width="160">
</p>

<h1 align="center">Rune</h1>

<p align="center">
  A small, sandboxed scripting platform for Discord.
</p>

Rune lets you upload scripts directly through Discord and run them as isolated WebAssembly modules.

Runes are written in familiar languages such as JavaScript and Python, while Rune provides a common Discord-facing API.

```javascript
if (message.content === "hello") {
    await message.reply("Hello!");
}
````

```python
if message.content == "hello":
    await message.reply("Hello!")
```

## Discord API

Rune subsets [NetCord](https://github.com/NetCordDev/NetCord) and ports it for each language. Currently all runes are registered as `MessageCreate` events.

For a `MessageCreate` rune, the only object passed is `message`, which exposes the following properties and methods.

JavaScript:

```javascript
message.id
message.channelId
message.content

message.author.id
message.author.username

await message.reply("Hello!")
```

Python:

```python
message.id
message.channel_id
message.content

message.author.id
message.author.username

await message.reply("Hello!")
```

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
