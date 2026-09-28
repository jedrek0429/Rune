#!/bin/sh
set -eu

source="$1"
output="$2"
project=/work/rust-rune
target=/work/rust-target
cargo_home=/work/cargo-home

rm -rf "$project" "$target" "$cargo_home"
mkdir -p "$project/src" "$cargo_home"
ln -s /usr/local/cargo/registry "$cargo_home/registry"

cp /opt/rune/rust/Cargo.toml "$project/Cargo.toml"
cp /opt/rune/rust/Cargo.lock "$project/Cargo.lock"
cp "$source" "$project/src/main.rs"

set +e
RUSTUP_HOME=/usr/local/rustup \
CARGO_HOME="$cargo_home" \
cargo build \
  --offline \
  --release \
  --manifest-path "$project/Cargo.toml" \
  --target-dir "$target" \
  2>"$project/stderr"
status=$?
set -e

sed \
  -e 's#/work/rust-rune/src/main.rs#/input/source.rs#g' \
  -e 's#src/main.rs#/input/source.rs#g' \
  "$project/stderr" >&2

if [ "$status" -ne 0 ]; then
  exit "$status"
fi

cp "$target/release/rune-program" "$output"
