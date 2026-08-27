import contextlib
import io
import json
import sys
import time

MAX_ACTIONS = 16
MAX_REPLY_BYTES = 2000


def project(value, key=""):
    if isinstance(value, str) and is_snowflake_key(key) and value.isdecimal():
        return int(value)
    if isinstance(value, list):
        return tuple(project(item) for item in value)
    if isinstance(value, dict):
        return {child_key: project(child_value, child_key) for child_key, child_value in value.items()}
    return value


def is_snowflake_key(key):
    return key == "id" or key.endswith("Id")


class Frozen:
    __slots__ = ("_values",)

    def __init__(self, values):
        object.__setattr__(self, "_values", {
            key: Frozen(value) if isinstance(value, dict) else value
            for key, value in values.items()
        })

    def __getattr__(self, name):
        try:
            return self._values[name]
        except KeyError as exc:
            raise AttributeError(name) from exc

    def __setattr__(self, name, value):
        raise AttributeError("Rune event payloads are read-only")


class Message(Frozen):
    __slots__ = ("_actions",)

    def __init__(self, values, actions):
        super().__init__(values)
        object.__setattr__(self, "_actions", actions)

    def reply(self, content):
        text = str(content)
        if len(text.encode("utf-8")) > MAX_REPLY_BYTES:
            raise ValueError(f"reply exceeds {MAX_REPLY_BYTES} UTF-8 bytes")
        if len(self._actions) >= MAX_ACTIONS:
            raise RuntimeError(f"Rune produced more than {MAX_ACTIONS} host actions")
        self._actions.append({"method": "message.reply", "arguments": {"content": text}})


compile("1 + 1", "<warmup>", "eval")
print('{"ready":true}', flush=True)

for line in sys.stdin:
    if not line.strip():
        continue

    started = time.perf_counter_ns()
    actions = []
    error = None

    try:
        envelope = json.loads(line)
        raw_payload = project(envelope["payload"])

        if envelope["eventType"] == "messageCreate":
            payload = Message(raw_payload, actions)
        else:
            payload = Frozen(raw_payload)

        globals_ = {
            "__builtins__": __builtins__,
            "event": payload,
        }
        if envelope["eventType"] == "messageCreate":
            globals_["message"] = payload

        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            exec(compile(envelope["source"], f'{envelope["runeName"]}.py', "exec"), globals_, globals_)
    except BaseException as caught:
        actions.clear()
        error = str(caught)

    duration_micros = (time.perf_counter_ns() - started) // 1000
    print(json.dumps({
        "actions": actions,
        "error": error,
        "durationMicros": duration_micros,
    }, separators=(",", ":")), flush=True)
