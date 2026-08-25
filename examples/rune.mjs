if (message.content === "!native-test") {
    if (typeof message.id !== "bigint" ||
        typeof message.channelId !== "bigint" ||
        typeof message.author.id !== "bigint" ||
        typeof message.author.username !== "string") {
        throw new Error("Rune.API projection failed");
    }
}
