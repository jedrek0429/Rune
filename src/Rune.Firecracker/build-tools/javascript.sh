#!/usr/bin/env bash
set -euo pipefail

[[ $# -eq 2 ]] || { echo 'usage: rune-build-javascript <source.js> <artifact>' >&2; exit 2; }

source="$1"
artifact="$2"
generated="/work/rune-generated.c"

# qjsc emits the C bootstrap plus compiled JavaScript bytecode.
qjsc -m -e -o "$generated" "$source"

# Link against the pinned QuickJS-NG runtime bundled into the build rootfs.
cc -O2 -D_GNU_SOURCE -DQUICKJS_NG_BUILD \
  -I/usr/local/include \
  "$generated" /usr/local/lib/libqjs.a \
  -lm -ldl -pthread -o "$artifact"
