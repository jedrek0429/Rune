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

        async function invoke(source, payload = defaultPayload()) {
            child.stdin.write(`${JSON.stringify({
                runeName: "worker-test",
                eventType: "messageCreate",
                source,
                payload,
            })}\n`);

            const response = await lines.next();
            assert.equal(response.done, false);
            return JSON.parse(response.value);
        }

        await run(invoke);
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

test("message.reply emits only the constrained host action and projects snowflakes as bigint", async () => {
    await withWorker(async (invoke) => {
        const result = await invoke(`
            if (typeof message.id !== "bigint") throw new Error("message.id was not bigint");
            if (typeof message.author.id !== "bigint") throw new Error("author.id was not bigint");
            message.reply(message.id.toString() + ":" + message.author.id.toString());
        `);

        assert.equal(result.error, null);
        assert.deepEqual(result.actions, [
            {
                method: "message.reply",
                arguments: {
                    content: "18446744073709551612:18446744073709551613",
                },
            },
        ]);
        assert.ok(result.durationMicros >= 0);
    });
});

test("a failed Rune never leaks host actions emitted before the failure", async () => {
    await withWorker(async (invoke) => {
        const result = await invoke(`
            message.reply("must not escape");
            throw new Error("boom");
        `);

        assert.deepEqual(result.actions, []);
        assert.equal(result.error, "boom");
    });
});

test("each invocation receives a fresh JavaScript context", async () => {
    await withWorker(async (invoke) => {
        const source = `
            globalThis.counter = (globalThis.counter ?? 0) + 1;
            message.reply(String(globalThis.counter));
        `;

        const first = await invoke(source);
        const second = await invoke(source);

        assert.equal(first.error, null);
        assert.equal(second.error, null);
        assert.equal(first.actions[0].arguments.content, "1");
        assert.equal(second.actions[0].arguments.content, "1");
    });
});

test("reply and action limits fail the invocation atomically", async () => {
    await withWorker(async (invoke) => {
        const oversized = await invoke(`message.reply("x".repeat(2001));`);
        assert.deepEqual(oversized.actions, []);
        assert.match(oversized.error, /reply exceeds 2000 UTF-8 bytes/);

        const tooMany = await invoke(`
            for (let i = 0; i < 17; i++) message.reply(String(i));
        `);
        assert.deepEqual(tooMany.actions, []);
        assert.match(tooMany.error, /more than 16 host actions/);
    });
});
