#!/usr/bin/env bash
set -euo pipefail

[[ $# -eq 2 ]] || { echo "usage: $0 <source.py> <artifact>" >&2; exit 2; }
source="$1"
artifact="$2"
work="$(dirname "$artifact")"
mpy="$work/rune.mpy"
launcher="$work/rune-python.c"

mpy-cross "$source" -o "$mpy"
python3 - "$mpy" "$launcher" <<'PY'
from pathlib import Path
import sys

data = Path(sys.argv[1]).read_bytes()
values = ",".join(str(byte) for byte in data)
Path(sys.argv[2]).write_text(f'''#include <stdint.h>\n#include "port/micropython_embed.h"\nstatic const uint8_t rune_mpy[] = {{{values}}};\nstatic char heap[64 * 1024];\nint main(void) {{\n    int stack_top;\n    mp_embed_init(heap, sizeof(heap), &stack_top);\n    mp_embed_exec_mpy(rune_mpy, sizeof(rune_mpy));\n    mp_embed_deinit();\n    return 0;\n}}\n''')
PY

cc -O2 -s \
  -I/opt/rune/micropython/embed \
  -I/opt/rune/micropython/embed/port \
  "$launcher" /opt/rune/micropython/libmicropython_embed.a \
  -o "$artifact"
