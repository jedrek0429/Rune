#!/usr/bin/env bash

rune_invocation_profile() {
  [[ $# -eq 0 ]] || return 2
  echo "1 192 32 3 32 128"
}

rune_build_profile() {
  case "${1:-}" in
    scriptc) echo "1 512 512 30 128 512" ;;
    clang) echo "1 512 512 20 128 256" ;;
    rust) echo "2 1024 512 45 128 256" ;;
    dotnet-aot) echo "2 2048 768 60 128 256" ;;
    python|ruby) echo "1 512 256 20 128 256" ;;
    *) return 2 ;;
  esac
}