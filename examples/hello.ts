if (message.content === "!hello") {
  await message.reply({
    content: `Hello, ${message.author.username}!`,
  });
}
