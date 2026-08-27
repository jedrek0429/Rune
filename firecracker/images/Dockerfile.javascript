FROM rust:1-alpine AS guest-builder
RUN apk add --no-cache musl-dev
WORKDIR /build
COPY native/Rune.Firecracker.Guest/Cargo.toml ./Cargo.toml
COPY native/Rune.Firecracker.Guest/src ./src
RUN cargo build --release

FROM node:24-alpine
RUN mkdir -p /opt/rune /proc /dev /tmp && echo javascript > /etc/rune-language
COPY --from=guest-builder /build/target/release/rune-firecracker-guest /sbin/rune-guest
COPY firecracker/guest/javascript/worker.mjs /opt/rune/worker.mjs
