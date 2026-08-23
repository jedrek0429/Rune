import readline from "node:readline";

const input = readline.createInterface({
    input: process.stdin,
    crlfDelay: Infinity
});

let rune = null;

function send(value) {
    process.stdout.write(JSON.stringify(value) + "\n");
}

function createMessage(invocation) {
    const data = invocation.message;

    return {
        id: data.id,
        channelId: data.channelId,
        content: data.content,

        author: {
            id: data.author.id,
            username: data.author.username
        },

        async reply(content) {
            send({
                type: "request",
                invocationId: invocation.invocationId,
                method: "message.reply",
                arguments: {
                    content
                }
            });
        }
    };
}

async function load(source) {
    const wrappedSource = `
        export default async function(message) {
            ${source}
        }
    `;

    const moduleUrl =
        "data:text/javascript;base64," +
        Buffer.from(wrappedSource).toString("base64");

    const module = await import(moduleUrl);

    rune = module.default;
}

for await (const line of input) {
    const message = JSON.parse(line);

    if (message.type === "shutdown") {
        break;
    }

    if (message.type === "load") {
        try {
            await load(message.source);

            send({
                type: "ready"
            });
        }
        catch (error) {
            process.stderr.write(
                `${error.stack ?? error}\n`
            );

            send({
                type: "load_error"
            });
        }

        continue;
    }

    if (message.type !== "message_create")
        continue;

    try {
        if (rune === null)
            throw new Error("No rune has been loaded.");

        await rune(createMessage(message));
    }
    catch (error) {
        process.stderr.write(
            `${error.stack ?? error}\n`
        );
    }
    finally {
        send({
            type: "complete",
            invocationId: message.invocationId
        });
    }
}
