#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

MAX_DETAILS_CHARS = 1200
MAX_DETAILS_LINES = 8

LANGUAGES = {
    "rust": ("Rust", "rune.rs"),
    "c": ("C", "rune.c"),
    "cpp": ("C++", "rune.cpp"),
    "javascript": ("JavaScript", "rune.js"),
    "typescript": ("TypeScript", "rune.ts"),
    "python": ("Python", "rune.py"),
}

ANSI = re.compile(r"\x1b\[[0-?]*[ -/]*[@-~]")
RESOURCE_MARKERS = re.compile(
    r"(cpu time limit exceeded|killed|cannot allocate memory|"
    r"no space left on device|too many open files|resource temporarily unavailable)",
    re.IGNORECASE,
)


def sanitise(raw: str, language: str) -> str:
    _, filename = LANGUAGES[language]
    text = ANSI.sub("", raw).replace("\r\n", "\n").replace("\r", "\n")

    for source in (
        "source.rs",
        "source.cpp",
        "source.c",
        "source.js",
        "source.ts",
        "source.py",
    ):
        text = text.replace(f"/input/{source}", filename)

    text = re.sub(r"/input/[^\s\"']+", "<input>", text)
    text = re.sub(r"/work/[^\s\"']+", "<build>", text)
    text = re.sub(r"/tmp/[^\s\"']+", "<tmp>", text)

    lines = [line.rstrip() for line in text.split("\n")]
    while lines and not lines[0].strip():
        lines.pop(0)
    while lines and not lines[-1].strip():
        lines.pop()

    return "\n".join(lines)


def location(text: str, filename: str) -> tuple[str | None, int | None, int | None]:
    normal = re.search(
        rf"(?<![\w.])({re.escape(filename)}):(\d+)(?::(\d+))?",
        text,
    )
    if normal:
        return (
            normal.group(1),
            int(normal.group(2)),
            int(normal.group(3)) if normal.group(3) else None,
        )

    python = re.search(
        rf'File "({re.escape(filename)})", line (\d+)',
        text,
    )
    if python:
        return python.group(1), int(python.group(2)), None

    return None, None, None


def primary_message(
    text: str,
    language: str,
    kind: str,
) -> tuple[str, str | None]:
    if kind == "timeout":
        return "Build exceeded the configured wall-time limit.", None
    if kind == "resource":
        return "Build exceeded a configured resource limit.", None
    if kind == "infrastructure":
        return "Build environment failed before compilation completed.", None

    patterns: list[re.Pattern[str]] = []

    if language == "rust":
        patterns.append(
            re.compile(r"^error(?:\[([^\]]+)\])?:\s*(.+)$", re.MULTILINE)
        )
    elif language in {"c", "cpp"}:
        patterns.append(
            re.compile(
                r"^rune\.(?:c|cpp):\d+(?::\d+)?:\s*"
                r"(?:fatal\s+)?error:\s*(.+)$",
                re.MULTILINE,
            )
        )
    elif language in {"javascript", "typescript"}:
        patterns.append(
            re.compile(r"(?:✘\s*)?\[ERROR\]\s*(.+)")
        )
    elif language == "python":
        patterns.append(
            re.compile(r"^(?:SyntaxError|IndentationError):\s*(.+)$", re.MULTILINE)
        )

    for pattern in patterns:
        match = pattern.search(text)
        if match:
            if language == "rust":
                return match.group(2).strip(), match.group(1)
            return match.group(1).strip(), None

    generic = re.search(
        r"^(?:.*?:\s*)?(?:fatal\s+)?(?:error|Error|SyntaxError):\s*(.+)$",
        text,
        re.MULTILINE,
    )
    if generic:
        return generic.group(1).strip(), None

    for line in text.splitlines():
        line = line.strip()
        if line:
            return line[:300], None

    return "Compiler rejected the Rune source.", None


def bounded_details(text: str) -> str | None:
    lines = [line for line in text.splitlines() if line.strip()]
    details = "\n".join(lines[:MAX_DETAILS_LINES]).strip()
    if not details:
        return None
    if len(details) > MAX_DETAILS_CHARS:
        details = details[:MAX_DETAILS_CHARS].rstrip() + "…"
    return details


def build_diagnostic(language: str, kind: str, raw: str) -> dict[str, object]:
    display_name, filename = LANGUAGES[language]
    cleaned = sanitise(raw, language)

    if kind == "compilation" and RESOURCE_MARKERS.search(cleaned):
        kind = "resource"

    file, line, column = location(cleaned, filename)
    message, code = primary_message(cleaned, language, kind)

    diagnostic: dict[str, object] = {
        "language": language,
        "kind": kind,
        "message": message,
    }

    if file is not None:
        diagnostic["file"] = file
    if line is not None:
        diagnostic["line"] = line
    if column is not None:
        diagnostic["column"] = column
    if code is not None:
        diagnostic["code"] = code

    details = bounded_details(cleaned)
    if details is not None:
        diagnostic["details"] = details

    diagnostic["displayLanguage"] = display_name
    return diagnostic


def render_pretty(diagnostic: dict[str, object]) -> str:
    display_language = str(diagnostic["displayLanguage"])
    kind = str(diagnostic["kind"])

    title = {
        "compilation": f"{display_language} compilation failed",
        "timeout": f"{display_language} build timed out",
        "resource": f"{display_language} build hit a resource limit",
        "infrastructure": f"{display_language} build infrastructure failed",
    }[kind]

    lines = [title]

    if "file" in diagnostic:
        where = f'{diagnostic["file"]}:{diagnostic["line"]}'
        if "column" in diagnostic:
            where += f':{diagnostic["column"]}'
        lines.extend(["", f"  --> {where}"])

    code = f' [{diagnostic["code"]}]' if "code" in diagnostic else ""
    lines.append(f'   = {diagnostic["message"]}{code}')

    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--language", choices=LANGUAGES, required=True)
    parser.add_argument(
        "--kind",
        choices=("compilation", "timeout", "resource", "infrastructure"),
        required=True,
    )
    parser.add_argument("--input", type=Path)
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args()

    raw = (
        args.input.read_text(encoding="utf-8", errors="replace")
        if args.input is not None and args.input.exists()
        else sys.stdin.read()
    )

    diagnostic = build_diagnostic(args.language, args.kind, raw)

    if args.json:
        public = {
            key: value
            for key, value in diagnostic.items()
            if key != "displayLanguage"
        }
        print(json.dumps(public, ensure_ascii=False, separators=(",", ":")))
    else:
        print(render_pretty(diagnostic))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
