#!/usr/bin/env bash
set -euo pipefail

[[ $# -eq 2 ]] || { echo "usage: $0 <source.js|source.ts> <artifact>" >&2; exit 2; }
source="$1"
artifact="$2"

mkdir -p /work/scriptc-cache
cp -a /opt/rune/scriptc-cache/. /work/scriptc-cache/
chmod 0700 /work/scriptc-cache
export SCRIPTC_CACHE_DIR=/work/scriptc-cache

if scriptc build "$source" -o "$artifact"; then
  exit 0
fi

rm -f "$artifact"
scriptc coverage "$source" --dynamic >/dev/null 2>&1 || exit 1
exec scriptc build "$source" --dynamic -o "$artifact"
