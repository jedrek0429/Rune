import vm from "node:vm";
import readline from "node:readline";

const rl = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
const encoder = new TextEncoder();
const MAX_ACTIONS = 16;
const MAX_REPLY_BYTES = 2000;

new vm.Script("1 + 1").runInNewContext(Object.create(null));
process.stdout.write('{"ready":true}\n');

for await (const line of rl) {
    if (!line) continue;

    const started = process.hrtime.bigint();
    const actions = [];
    let error = null;

    try {
        const envelope = JSON.parse(line);
        const value = project(envelope.payload);

        if (envelope.eventType === "messageCreate") {
            Object.defineProperty(value, "reply", {
                enumerable: false,
                configurable: false,
                writable: false,
                value(content) {
                    const text = String(content);
                    if (encoder.encode(text).length > MAX_REPLY_BYTES) {
                        throw new Error(`reply exceeds ${MAX_REPLY_BYTES} UTF-8 bytes`);
                    }
                    if (actions.length >= MAX_ACTIONS) {
                        throw new Error(`Rune produced more than ${MAX_ACTIONS} host actions`);
                    }
                    actions.push({ method: "message.reply", arguments: { content: text } });
                },
            });
        }

        deepFreeze(value);

        const sandbox = Object.create(null);
        Object.defineProperty(sandbox, "event", { value, writable: false });
        if (envelope.eventType === "messageCreate") {
            Object.defineProperty(sandbox, "message", { value, writable: false });
        }

        const context = vm.createContext(sandbox, {
            name: `rune:${envelope.runeName}`,
            codeGeneration: { strings: true, wasm: false },
        });

        new vm.Script(envelope.source, {
            filename: `${envelope.runeName}.js`,
        }).runInContext(context, { timeout: 1500 });
    } catch (caught) {
        actions.length = 0;
        error = caught instanceof Error ? caught.message : String(caught);
    }

    const durationMicros = Number((process.hrtime.bigint() - started) / 1000n);
    process.stdout.write(JSON.stringify({ actions, error, durationMicros }) + "\n");
}

function project(value, key = "") {
    if (typeof value === "string" && isSnowflakeKey(key) && /^\d+$/.test(value)) {
        return BigInt(value);
    }
    if (Array.isArray(value)) {
        return value.map((item) => project(item));
    }
    if (value && typeof value === "object") {
        const result = Object.create(null);
        for (const [childKey, childValue] of Object.entries(value)) {
            result[childKey] = project(childValue, childKey);
        }
        return result;
    }
    return value;
}

function deepFreeze(value) {
    if (!value || typeof value !== "object") return value;
    for (const child of Object.values(value)) deepFreeze(child);
    return Object.freeze(value);
}

function isSnowflakeKey(key) {
    return key === "id" || key.endsWith("Id");
}
