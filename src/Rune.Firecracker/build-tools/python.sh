#!/usr/bin/env bash
set -euo pipefail

[[ $# -eq 2 ]] || {
    echo "usage: $0 <source.py> <artifact>" >&2
    exit 2
}

source="$1"
artifact="$2"
work="$(dirname "$artifact")"

mpy="$work/rune.mpy"
launcher="$work/rune-python.c"

# Compile Python source to MicroPython bytecode.
mpy-cross "$source" -o "$mpy"

# Turn the .mpy file into a byte array for the tiny native launcher.
python3 - "$mpy" "$launcher" <<'PY'
from pathlib import Path
import sys

data = Path(sys.argv[1]).read_bytes()
values = ",".join(str(byte) for byte in data)

Path(sys.argv[2]).write_text(f'''\
#include <stdint.h>
#include "port/micropython_embed.h"

static const uint8_t rune_mpy[] = {{{values}}};
static char heap[64 * 1024];

int main(void) {{
    int stack_top;

    mp_embed_init(heap, sizeof(heap), &stack_top);
    mp_embed_exec_mpy(rune_mpy, sizeof(rune_mpy));
    mp_embed_deinit();

    return 0;
}}
''')
PY



cc -O2 -s \
    -I/opt/rune/micropython-embed/micropython_embed \
    "$launcher" \
    /opt/rune/libmicropython_embed.a \
    -lm \
    -o "$artifact"

