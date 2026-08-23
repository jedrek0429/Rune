import asyncio
import json
import sys
import textwrap
import traceback


protocol_output = sys.stdout

# User print() calls must not corrupt the JSON protocol on stdout.
sys.stdout = sys.stderr

rune = None


def send(value):
    protocol_output.write(json.dumps(value) + "\n")
    protocol_output.flush()


class RuneUser:
    def __init__(self, data):
        self.id = data["id"]
        self.username = data["username"]


class RuneMessage:
    def __init__(self, invocation):
        data = invocation["message"]

        self.id = data["id"]
        self.channel_id = data["channelId"]
        self.content = data["content"]
        self.author = RuneUser(data["author"])

        self._invocation_id = invocation["invocationId"]

    async def reply(self, content):
        send({
            "type": "request",
            "invocationId": self._invocation_id,
            "method": "message.reply",
            "arguments": {
                "content": str(content)
            }
        })


def load(source):
    global rune

    body = textwrap.indent(source, "    ")

    if not body:
        body = "    pass"

    wrapped_source = (
        "async def __rune__(message):\n"
        + body
    )

    namespace = {}

    exec(
        compile(
            wrapped_source,
            "<rune>",
            "exec"
        ),
        namespace
    )

    rune = namespace["__rune__"]


async def execute(invocation):
    if rune is None:
        raise RuntimeError("No rune has been loaded.")

    message = RuneMessage(invocation)

    await rune(message)


async def main():
    while True:
        line = await asyncio.to_thread(
            sys.stdin.readline
        )

        if not line:
            return

        try:
            message = json.loads(line)

            if message["type"] == "shutdown":
                return

            if message["type"] == "load":
                try:
                    load(message["source"])

                    send({
                        "type": "ready"
                    })
                except Exception:
                    traceback.print_exc(file=sys.stderr)

                    send({
                        "type": "load_error"
                    })

                continue

            if message["type"] != "message_create":
                continue

            try:
                await execute(message)
            except Exception:
                traceback.print_exc(file=sys.stderr)
            finally:
                send({
                    "type": "complete",
                    "invocationId": message["invocationId"]
                })

        except Exception:
            traceback.print_exc(file=sys.stderr)


asyncio.run(main())
