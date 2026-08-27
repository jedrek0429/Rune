pub fn rune(message: Message) {
    if message.content == "!firecracker" {
        message.reply(format!(
            "Rust from a disposable microVM, {}.",
            message.author.username
        ));
    }
}
