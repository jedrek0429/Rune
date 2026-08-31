#!/usr/bin/env bash
set -euo pipefail

[[ $# -eq 2 ]] || { echo "usage: $0 <source.js|source.ts> <artifact>" >&2; exit 2; }
source="$1"
artifact="$2"

# ScriptC's strict persistent-cache validation is more expensive than compiling
# the small base runtime in Rune's disposable, immutable build VM. Keep the
# common static path cache-free; only materialise the prewarmed cache when the
# program genuinely needs the dynamic engine.
if SCRIPTC_NO_CACHE=1 scriptc build "$source" -o "$artifact"; then
  exit 0
fi

rm -f "$artifact"
scriptc coverage "$source" --dynamic >/dev/null 2>&1 || exit 1

mkdir -p /work/scriptc-cache
find /cache-seed -mindepth 1 -maxdepth 1 ! -name lost+found -exec cp -R -- {} /work/scriptc-cache/ \;
chmod 0700 /work/scriptc-cache
export SCRIPTC_CACHE_DIR=/work/scriptc-cache

exec scriptc build "$source" --dynamic -o "$artifact"
