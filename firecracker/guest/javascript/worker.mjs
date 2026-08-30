import crypto from "node:crypto";
import vm from "node:vm";
import readline from "node:readline";

const rl = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
const encoder = new TextEncoder();
const MAX_HOST_CALLS = 16;
const MAX_REPLY_BYTES = 2000;
const pendingHostCalls = new Map();
const activeInvocations = new Set();

new vm.Script("1 + 1").runInNewContext(Object.create(null));
write({ ready: true });

rl.on("line", (line) => {
    if (!line) return;

    let message;
    try {
        message = JSON.parse(line);
    } catch (error) {
        write({ type: "protocolError", error: errorMessage(error) });
        return;
    }

    if (message.type === "hostResult" || message.type === "hostError") {
        completeHostCall(message);
        return;
    }

    if (message.type !== "invoke") {
        write({ type: "protocolError", error: `unsupported message type: ${message.type}` });
        return;
    }

    void invoke(message);
});

async function invoke(envelope) {
    const started = process.hrtime.bigint();
    const invocationId = String(envelope.invocationId);

    if (activeInvocations.has(invocationId)) {
        write({
            type: "completed",
            invocationId,
            error: "invocation is already active",
            durationMicros: 0,
        });
        return;
    }

    activeInvocations.add(invocationId);
    let hostCalls = 0;

    try {
        const makeReply = (receiverId) => async (replyMessage) => {
            const content = replyMessage?.content;
            if (content != null && encoder.encode(String(content)).length > MAX_REPLY_BYTES) {
                throw new Error(`reply exceeds ${MAX_REPLY_BYTES} UTF-8 bytes`);
            }
            if (hostCalls >= MAX_HOST_CALLS) {
                throw new Error(`Rune produced more than ${MAX_HOST_CALLS} host calls`);
            }
            hostCalls += 1;

            const result = await hostCall({
                invocationId,
                method: "NetCord.Rest.RestMessage.ReplyAsync",
                receiverId,
                arguments: {
                    replyMessage: {
                        content: content == null ? null : String(content),
                    },
                },
            });

            return projectRestMessage(result, makeReply);
        };

        const message = envelope.eventType === "messageCreate"
            ? projectRestMessage(envelope.payload, makeReply)
            : deepFreeze(project(envelope.payload));

        const sandbox = Object.create(null);
        Object.defineProperty(sandbox, "event", { value: message, writable: false });
        if (envelope.eventType === "messageCreate") {
            Object.defineProperty(sandbox, "message", { value: message, writable: false });
        }

        const context = vm.createContext(sandbox, {
            name: `rune:${envelope.runeName}`,
            codeGeneration: { strings: true, wasm: false },
        });

        new vm.Script(envelope.source, {
            filename: `${envelope.runeName}.js`,
        }).runInContext(context, { timeout: 1500 });

        if (typeof context.handle === "function") {
            await context.handle(message);
        }

        write({
            type: "completed",
            invocationId,
            error: null,
            durationMicros: elapsedMicros(started),
        });
    } catch (error) {
        rejectInvocationHostCalls(invocationId, error);
        write({
            type: "completed",
            invocationId,
            error: errorMessage(error),
            durationMicros: elapsedMicros(started),
        });
    } finally {
        activeInvocations.delete(invocationId);
    }
}

function hostCall({ invocationId, method, receiverId, arguments: args }) {
    const requestId = crypto.randomUUID();

    return new Promise((resolve, reject) => {
        pendingHostCalls.set(requestId, { invocationId, resolve, reject });
        write({
            type: "hostCall",
            invocationId,
            requestId,
            method,
            receiverId,
            arguments: args,
        });
    });
}

function completeHostCall(message) {
    const requestId = String(message.requestId);
    const pending = pendingHostCalls.get(requestId);

    if (!pending || pending.invocationId !== String(message.invocationId)) {
        write({
            type: "protocolError",
            error: "host response does not match an active request",
        });
        return;
    }

    pendingHostCalls.delete(requestId);
    if (message.type === "hostError") {
        pending.reject(new Error(String(message.error ?? "host call failed")));
    } else {
        pending.resolve(message.result);
    }
}

function rejectInvocationHostCalls(invocationId, cause) {
    for (const [requestId, pending] of pendingHostCalls) {
        if (pending.invocationId !== invocationId) continue;
        pendingHostCalls.delete(requestId);
        pending.reject(cause);
    }
}

function projectRestMessage(value, makeReply) {
    const projected = project(value);
    const receiverId = String(value?.id ?? "");
    Object.defineProperty(projected, "reply", {
        enumerable: false,
        configurable: false,
        writable: false,
        value: makeReply(receiverId),
    });
    return deepFreeze(projected);
}

function errorMessage(caught) {
    try {
        if (
            caught &&
            (typeof caught === "object" || typeof caught === "function") &&
            typeof caught.message === "string"
        ) {
            return caught.message;
        }
    } catch {
        // A Rune can throw an object with a hostile message getter.
    }

    try {
        return String(caught);
    } catch {
        return "Rune threw an unprintable value";
    }
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

function elapsedMicros(started) {
    return Number((process.hrtime.bigint() - started) / 1000n);
}

function write(value) {
    process.stdout.write(`${JSON.stringify(value)}\n`);
}
