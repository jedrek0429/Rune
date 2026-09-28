if (message.content === "!rune") {
  await message.reply({
    content:
      `Rune API is working.\n` +
      `Author: ${message.author.username}\n` +
      `Message: ${message.id}\n` +
      `Channel: ${message.channelId}`,
  });
}
