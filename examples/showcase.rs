if message.content == "!rune" {
    message.reply(
        host,
        ReplyMessageProperties {
            content: Some(format!(
                "Rune API is working.\nAuthor: {}\nMessage: {}\nChannel: {}",
                message.author.username,
                message.id,
                message.channel_id,
            )),
        },
    )?;
}
