// Equivalent to the wrapper emitted by JavaScriptRuneCompiler for the
// MessageCreate benchmark. Keep this fixture local to the experiment.
class User {
    constructor(value) {
        this.id = value.id;
        this.username = value.username;
        Object.freeze(this);
    }
}

class Message {
    constructor(value) {
        this.id = value.id;
        this.channelId = value.channelId;
        this.content = value.content;
        this.author = value.author == null ? null : new User(value.author);
        Object.freeze(this);
    }
}

export function handle(value) {
    const message = new Message(value);
    if (message.content === "!native-test") {
        void message.author.username;
    }
}
