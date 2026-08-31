#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck source=/dev/null
source "$repo_root/firecracker/resource-policy.sh"

assert_eq() {
  local expected="$1"
  local actual="$2"
  local message="$3"
  if [[ "$expected" != "$actual" ]]; then
    echo "$message: expected '$expected', got '$actual'" >&2
    exit 1
  fi
}

assert_eq "1 192 32 3 32 128" "$(rune_invocation_profile)" "Rune invocation profile"
assert_eq "1 512 512 30 128 512" "$(rune_build_profile scriptc)" "ScriptC build profile"
assert_eq "1 512 512 20 128 256" "$(rune_build_profile clang)" "Clang build profile"
assert_eq "2 1024 512 45 128 256" "$(rune_build_profile rust)" "Rust build profile"
assert_eq "2 2048 768 60 128 256" "$(rune_build_profile dotnet-aot)" ".NET AOT build profile"
assert_eq "1 512 256 20 128 256" "$(rune_build_profile python)" "MicroPython build profile"
assert_eq "1 512 256 20 128 256" "$(rune_build_profile ruby)" "mruby build profile"

snapshot="$repo_root/firecracker/build-snapshot.sh"
builder="$repo_root/firecracker/run-build-vm.sh"
rootfs_builder="$repo_root/firecracker/build-rootfs.sh"
scriptc_warmer="$repo_root/firecracker/warm-scriptc-cache.sh"
assert_eq "1 192" "$(bash "$snapshot" --print-profile)" "Rune snapshot profile"
assert_eq "1 512 512 30 128 512" "$(bash "$builder" --print-profile scriptc)" "ScriptC build launcher profile"
assert_eq "2 1024 512 45 128 256" "$(bash "$builder" --print-profile rust)" "Rust build launcher profile"
assert_eq "2 2048 768 60 128 256" "$(bash "$builder" --print-profile dotnet-aot)" ".NET AOT build launcher profile"

grep -q 'rune.fd_limit=512' "$scriptc_warmer" || { echo "ScriptC cache warm must allow its parallel dependency rechecks without hitting EMFILE" >&2; exit 1; }
grep -q 'e2fsck -p "$cache"' "$scriptc_warmer" || { echo "ScriptC cache seed must replay and clear its journal before read-only reuse" >&2; exit 1; }

if bash "$snapshot" python >/dev/null 2>&1; then
  echo "execution snapshots must not be language-specific" >&2
  exit 1
fi
if bash "$builder" --print-profile node >/dev/null 2>&1; then
  echo "Node must not be a Rune build pool" >&2
  exit 1
fi
if grep -R -q '/network-interfaces' "$repo_root/firecracker" --include='*.sh'; then
  echo "Firecracker Rune VMs must not configure a network interface" >&2
  exit 1
fi
for launcher in "$snapshot" "$builder" "$scriptc_warmer"; do
  grep -q 'api_put /entropy' "$launcher" || { echo "every Rune VM must attach a Firecracker entropy device" >&2; exit 1; }
done

grep -q 'timeout .*wall_seconds' "$builder" || { echo "build VM launcher must enforce host wall time" >&2; exit 1; }
grep -q 'truncate -s .*disk_mib.*scratch' "$builder" || { echo "build VM launcher must bound scratch disk" >&2; exit 1; }
grep -q 'rune.language=' "$builder" || { echo "build VM must receive language" >&2; exit 1; }
grep -q 'drive_id.*input' "$builder" && grep -q 'is_read_only.*true' "$builder" || { echo "build input must be read-only" >&2; exit 1; }
grep -q 'drive_id.*cache_seed' "$builder" && grep -q 'is_read_only.*true' "$builder" || { echo "ScriptC cache seed must enter build VMs read-only with a valid Firecracker resource ID" >&2; exit 1; }
grep -q 'size <= 16777216' "$builder" || { echo "artifact must be capped at 16 MiB" >&2; exit 1; }
grep -q 'sha256sum' "$builder" || { echo "artifact store must be content-addressed" >&2; exit 1; }
grep -q 'e2fsck -p "$scratch"' "$builder" || { echo "build scratch must be journal-finalized after the VM stops" >&2; exit 1; }
grep -q 'dump -p /diagnostics.txt' "$builder" || { echo "build launcher must preserve guest compiler diagnostics" >&2; exit 1; }

dockerfile="$repo_root/firecracker/images/Dockerfile.build"
build_guest="$repo_root/native/Rune.Firecracker.BuildGuest/src/main.rs"
invocation_dockerfile="$repo_root/firecracker/images/Dockerfile.invocation"
scriptc_builder="$repo_root/firecracker/build-tools/scriptc.sh"
grep -q 'scriptc)' "$dockerfile" && grep -q 'build-essential' "$dockerfile" && grep -q 'clang' "$dockerfile" || { echo "ScriptC build image must include Linux headers, assembler and linker tooling" >&2; exit 1; }
grep -q 'ARG SCRIPTC_VERSION=0\.0\.35' "$dockerfile" || { echo "ScriptC compiler version must be pinned" >&2; exit 1; }
grep -Fq '"scriptc@$SCRIPTC_VERSION"' "$dockerfile" || { echo "ScriptC install must use the pinned compiler version" >&2; exit 1; }
if grep -Eq 'npm install .*scriptc scriptc([ ;]|$)' "$dockerfile"; then
  echo "ScriptC install must not float to the latest package" >&2
  exit 1
fi
grep -q 'build/rust) base=rust:1-bookworm' "$rootfs_builder" || { echo "Rust build rootfs must use the pinned Rust toolchain image" >&2; exit 1; }
grep -Fq 'RUSTUP_HOME=/usr/local/rustup CARGO_HOME=/usr/local/cargo exec /usr/local/cargo/bin/rustc "$@"' "$dockerfile" || { echo "Rust compiler must remain reachable after the build guest sanitises its environment" >&2; exit 1; }
grep -q 'dotnet publish.*PublishAot=true' "$dockerfile" || { echo ".NET build image must prewarm Native AOT assets before network is removed" >&2; exit 1; }
grep -q 'NUGET_PACKAGES=/opt/rune/nuget' "$dockerfile" || { echo ".NET build image must expose its prewarmed packages outside root home" >&2; exit 1; }
grep -q 'NUGET_PACKAGES.*opt/rune/nuget' "$build_guest" || { echo "build guest must use the prewarmed NuGet cache" >&2; exit 1; }
grep -q 'setgroups(0' "$build_guest" || { echo "build guest must clear supplementary groups before dropping privileges" >&2; exit 1; }
grep -q 'libc::sync()' "$build_guest" || { echo "build guest must flush scratch before reporting completion" >&2; exit 1; }
if grep -q 'mount("devtmpfs", "/dev"' "$build_guest"; then
  echo "build guest must use the kernel-provided /dev mount" >&2
  exit 1
fi
if grep -q 'scriptc cache warm' "$dockerfile"; then
  echo "ScriptC cache must not be warmed before the final rootfs exists" >&2
  exit 1
fi
[[ -f "$scriptc_warmer" ]] || { echo "final-rootfs ScriptC cache warmer is missing" >&2; exit 1; }
grep -Fq 'for profile in ["runtime", "dynamic"]' "$build_guest" || { echo "build guest must prewarm ScriptC runtime and dynamic profiles serially" >&2; exit 1; }
grep -Fq '.args(["cache", "warm", profile])' "$build_guest" || { echo "ScriptC warm profiles must run as independent processes" >&2; exit 1; }
if grep -Fq '.args(["cache", "warm", "runtime", "dynamic"])' "$build_guest"; then
  echo "ScriptC profiles must not share one concurrent warm workspace" >&2
  exit 1
fi
grep -Fq 'profiles: [process.argv[1]]' "$build_guest" || { echo "ScriptC cache diagnostics must diagnose only the failed profile" >&2; exit 1; }
grep -q 'rune.cache_warm=scriptc' "$scriptc_warmer" || { echo "ScriptC warmer must boot the final build rootfs in cache-warm mode" >&2; exit 1; }
grep -q 'chmod 0444.*cache' "$scriptc_warmer" || { echo "ScriptC cache seed must be frozen after warming" >&2; exit 1; }
grep -q 'warm-scriptc-cache.sh' "$rootfs_builder" || { echo "ScriptC rootfs build must warm its final cache seed" >&2; exit 1; }
grep -q 'rune-build-scriptc' "$build_guest" || { echo "JS/TS builds must use the ScriptC policy wrapper" >&2; exit 1; }
grep -q 'SCRIPTC_NO_CACHE=1 scriptc build "$source" -o "$artifact"' "$scriptc_builder" || { echo "ScriptC static builds must bypass expensive persistent-cache validation" >&2; exit 1; }
grep -q 'scriptc coverage "$source" --dynamic' "$scriptc_builder" || { echo "ScriptC wrapper must validate dynamic fallback" >&2; exit 1; }
grep -q 'scriptc build "$source" --dynamic -o "$artifact"' "$scriptc_builder" || { echo "ScriptC wrapper must use dynamic mode only as fallback" >&2; exit 1; }
static_line="$(grep -n 'SCRIPTC_NO_CACHE=1 scriptc build' "$scriptc_builder" | cut -d: -f1)"
cache_line="$(grep -n 'mkdir -p /work/scriptc-cache' "$scriptc_builder" | cut -d: -f1)"
[[ -n "$static_line" && -n "$cache_line" && "$static_line" -lt "$cache_line" ]] || { echo "ScriptC cache must be materialised only after the static path fails" >&2; exit 1; }
grep -q 'find /cache-seed' "$scriptc_builder" && grep -q '! -name lost+found' "$scriptc_builder" || { echo "ScriptC wrapper must copy only cache payload and exclude ext4 bookkeeping" >&2; exit 1; }
grep -q 'chmod 0700 /work/scriptc-cache' "$scriptc_builder" || { echo "per-build ScriptC cache must stay private" >&2; exit 1; }
grep -q 'SCRIPTC_CACHE_DIR=/work/scriptc-cache' "$scriptc_builder" || { echo "dynamic ScriptC builds must use writable bounded scratch for cache mutations" >&2; exit 1; }
grep -q 'MICROPY_PERSISTENT_CODE_LOAD' "$dockerfile" || { echo "MicroPython embed must load precompiled bytecode" >&2; exit 1; }
grep -q 'rune-build-python' "$build_guest" || { echo "Python builds must use the executable MicroPython packager" >&2; exit 1; }
grep -q 'rune-build-ruby' "$build_guest" || { echo "Ruby builds must use the executable mruby packager" >&2; exit 1; }
grep -q 'libunwind8' "$invocation_dockerfile" || { echo "execution image must include the Native AOT unwind runtime" >&2; exit 1; }
if grep -Eq 'python3|ruby|rune-runtime|RUNTIME' "$invocation_dockerfile"; then
  echo "invocation image must be language-agnostic" >&2
  exit 1
fi

ci="$repo_root/.github/workflows/ci.yml"
grep -q 'firecracker-ci/' "$ci" && grep -q 'vmlinux-6\.1' "$ci" || { echo "Firecracker smoke must use a current supported 6.1 guest kernel" >&2; exit 1; }

e2e="$repo_root/tests/firecracker/e2e.sh"
[[ -x "$e2e" ]] || { echo "eight-language Firecracker e2e harness is missing" >&2; exit 1; }
for language in javascript typescript python ruby rust c cpp csharp; do
  grep -q "^  $language)" "$e2e" || { echo "e2e harness does not cover $language" >&2; exit 1; }
done
grep -q 'rune:invocations' "$e2e" || { echo "e2e must use the shared invocation stream" >&2; exit 1; }
if grep -q 'rune:invocations:' "$e2e"; then
  echo "e2e must not partition invocation streams by language" >&2
  exit 1
fi

echo "resource policy tests passed"
