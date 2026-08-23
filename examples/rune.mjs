if (message.content === "!ping") {
    await message.reply("one");
    await message.reply("two");
    await message.reply("three");
    await message.reply(`Hi, ${message.author.username}!`);
}
