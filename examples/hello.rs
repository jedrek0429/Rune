if message.content == "!hello" {
    message.reply(
        ReplyMessageProperties {
            content: Some(format!("Hello, {}!", message.author.username)),
        },
    )?;
}
