import json
import pathlib
import subprocess
import sys
import time

MAX_ACTIONS = 16
MAX_REPLY_BYTES = 2000
TMP = pathlib.Path("/tmp")


def rust_string(value):
    numbers = ",".join(str(byte) for byte in value.encode("utf-8"))
    return f"String::from_utf8(vec![{numbers}]).unwrap()"


def build_source(envelope):
    payload = envelope["payload"]
    author = payload["author"]

    return f'''\
use std::sync::{{Mutex, OnceLock}};

#[derive(Clone, Debug)]
pub struct User {{
    pub id: u64,
    pub username: String,
}}

#[derive(Clone, Debug)]
pub struct Message {{
    pub id: u64,
    pub channel_id: u64,
    pub content: String,
    pub author: User,
}}

static RUNE_REPLIES: OnceLock<Mutex<Vec<String>>> = OnceLock::new();

impl Message {{
    pub fn reply(&self, content: impl Into<String>) {{
        RUNE_REPLIES.get_or_init(|| Mutex::new(Vec::new()))
            .lock().unwrap().push(content.into());
    }}
}}

{envelope["source"]}

fn main() {{
    let message = Message {{
        id: {int(payload["id"])},
        channel_id: {int(payload["channelId"])},
        content: {rust_string(payload["content"])},
        author: User {{
            id: {int(author["id"])},
            username: {rust_string(author["username"])},
        }},
    }};

    rune(message);

    if let Some(replies) = RUNE_REPLIES.get() {{
        for reply in replies.lock().unwrap().iter() {{
            print!("RUNE_REPLY:");
            for byte in reply.as_bytes() {{
                print!("{{:02x}}", byte);
            }}
            println!();
        }}
    }}
}}
'''


def warm_rustc():
    source = TMP / "rune-warm.rs"
    binary = TMP / "rune-warm"
    source.write_text("fn main() {}\n", encoding="utf-8")
    subprocess.run(
        ["/usr/bin/rustc", "--edition=2024", str(source), "-o", str(binary)],
        stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        check=True,
        timeout=20.0,
    )
    source.unlink(missing_ok=True)
    binary.unlink(missing_ok=True)


warm_rustc()
print('{"ready":true}', flush=True)

for line in sys.stdin:
    if not line.strip():
        continue

    started = time.perf_counter_ns()
    actions = []
    error = None
    source_path = None
    binary_path = None

    try:
        envelope = json.loads(line)
        if envelope["eventType"] != "messageCreate":
            raise RuntimeError("Rust Firecracker prototype currently supports MessageCreate runes")

        token = "".join(ch for ch in envelope["executionId"] if ch.isalnum())[:32]
        source_path = TMP / f"rune-{token}.rs"
        binary_path = TMP / f"rune-{token}"
        source_path.write_text(build_source(envelope), encoding="utf-8")

        compiled = subprocess.run(
            ["/usr/bin/rustc", "--edition=2024", "-O", str(source_path), "-o", str(binary_path)],
            stdin=subprocess.DEVNULL,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.PIPE,
            text=True,
            timeout=4.0,
        )
        if compiled.returncode != 0:
            diagnostic = compiled.stderr.replace(str(source_path), "<rune>")
            raise RuntimeError(diagnostic[-4000:])

        executed = subprocess.run(
            [str(binary_path)],
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            timeout=1.0,
        )
        if executed.returncode != 0:
            raise RuntimeError(executed.stderr[-2000:] or f"Rune exited with status {executed.returncode}")

        for output_line in executed.stdout.splitlines():
            if not output_line.startswith("RUNE_REPLY:"):
                continue
            text = bytes.fromhex(output_line.removeprefix("RUNE_REPLY:")).decode("utf-8")
            if len(text.encode("utf-8")) > MAX_REPLY_BYTES:
                raise RuntimeError("reply exceeded output limit")
            if len(actions) >= MAX_ACTIONS:
                raise RuntimeError("Rune produced too many host actions")
            actions.append({"method": "message.reply", "arguments": {"content": text}})
    except BaseException as caught:
        actions.clear()
        error = str(caught)
    finally:
        for path in (source_path, binary_path):
            if path is not None:
                try:
                    path.unlink(missing_ok=True)
                except OSError:
                    pass

    duration_micros = (time.perf_counter_ns() - started) // 1000
    print(json.dumps({
        "actions": actions,
        "error": error,
        "durationMicros": duration_micros,
    }, separators=(",", ":")), flush=True)
