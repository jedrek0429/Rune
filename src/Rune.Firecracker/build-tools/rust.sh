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

CARGO_HOME=/usr/local/cargo cargo build   --offline   --release   --manifest-path "$project/Cargo.toml"   --target-dir "$target"

cp "$target/release/rune-program" "$output"
