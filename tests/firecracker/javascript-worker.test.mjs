import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import readline from "node:readline";
import test from "node:test";
import { fileURLToPath } from "node:url";

const workerPath = fileURLToPath(
    new URL("../../firecracker/guest/javascript/worker.mjs", import.meta.url),
);

async function withWorker(run) {
    const child = spawn(process.execPath, [workerPath], {
        stdio: ["pipe", "pipe", "inherit"],
    });
    const output = readline.createInterface({ input: child.stdout });
    const lines = output[Symbol.asyncIterator]();

    try {
        const ready = await lines.next();
        assert.equal(ready.done, false);
        assert.deepEqual(JSON.parse(ready.value), { ready: true });

        async function send(message) {
            child.stdin.write(`${JSON.stringify(message)}\n`);
            const response = await lines.next();
            assert.equal(response.done, false);
            return JSON.parse(response.value);
        }

        async function invoke(source, payload = defaultPayload()) {
            return send({
                type: "invoke",
                invocationId: "00000000-0000-0000-0000-000000000001",
                runeName: "worker-test",
                eventType: "messageCreate",
                source,
                payload,
            });
        }

        await run({ invoke, send, lines, child });
    } finally {
        output.close();
        child.kill();
    }
}

function defaultPayload() {
    return {
        id: "18446744073709551612",
        channelId: "18446744073709551611",
        content: "hello",
        author: {
            id: "18446744073709551613",
            username: "Ada",
        },
    };
}

test("message.reply suspends for a correlated host response and returns the projected RestMessage", async () => {
    await withWorker(async ({ send, lines, child }) => {
        child.stdin.write(`${JSON.stringify({
            type: "invoke",
            invocationId: "00000000-0000-0000-0000-000000000001",
            runeName: "worker-test",
            eventType: "messageCreate",
            source: `
                globalThis.handle = async () => {
                    const reply = await message.reply({ content: "Hello" });
                    if (typeof reply.id !== "bigint") throw new Error("reply.id was not bigint");
                    if (reply.content !== "Hello") throw new Error("unexpected reply content");
                };
            `,
            payload: defaultPayload(),
        })}\n`);

        const requestLine = await lines.next();
        assert.equal(requestLine.done, false);
        const request = JSON.parse(requestLine.value);
        assert.equal(request.type, "hostCall");
        assert.equal(request.invocationId, "00000000-0000-0000-0000-000000000001");
        assert.equal(request.method, "NetCord.Rest.RestMessage.ReplyAsync");
        assert.equal(typeof request.requestId, "string");
        assert.deepEqual(request.arguments, { replyMessage: { content: "Hello" } });

        const completion = await send({
            type: "hostResult",
            invocationId: request.invocationId,
            requestId: request.requestId,
            result: {
                id: "18446744073709551610",
                channelId: "18446744073709551611",
                content: "Hello",
                author: {
                    id: "18446744073709551613",
                    username: "Ada",
                },
            },
        });

        assert.equal(completion.type, "completed");
        assert.equal(completion.invocationId, request.invocationId);
        assert.equal(completion.error, null);
    });
});

test("each invocation receives a fresh JavaScript context", async () => {
    await withWorker(async ({ invoke }) => {
        const source = `globalThis.counter = (globalThis.counter ?? 0) + 1;`;

        const first = await invoke(source);
        const second = await invoke(source);

        assert.equal(first.error, null);
        assert.equal(second.error, null);
    });
});
