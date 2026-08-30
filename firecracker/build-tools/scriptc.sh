#!/usr/bin/env bash
set -euo pipefail

[[ $# -eq 2 ]] || { echo "usage: $0 <source.js|source.ts> <artifact>" >&2; exit 2; }
source="$1"
artifact="$2"

if scriptc build "$source" -o "$artifact"; then
  exit 0
fi

rm -f "$artifact"
scriptc coverage "$source" --dynamic >/dev/null 2>&1 || exit 1
exec scriptc build "$source" --dynamic -o "$artifact"
