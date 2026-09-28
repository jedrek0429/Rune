if message.content == "!hello" {
    message.reply(
        host,
        ReplyMessageProperties {
            content: Some(format!("Hello, {}!", message.author.username)),
        },
    )?;
}
