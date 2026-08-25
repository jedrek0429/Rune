pub fn rune(message: Message) {
    if message.content == "!native-test" {
        assert!(message.id > 0);
        assert!(message.channel_id > 0);
        assert!(message.author.id > 0);
        assert!(!message.author.username.is_empty());
    }
}
