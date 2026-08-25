if message.content == "!native-test":
    if not isinstance(message.id, int):
        raise RuntimeError("message.id was not projected as an integer")
    if not isinstance(message.author.username, str):
        raise RuntimeError("message.author.username was not projected as text")
