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
assert_eq "1 512 512 30 128 256" "$(rune_build_profile scriptc)" "ScriptC build profile"
assert_eq "1 512 512 20 128 256" "$(rune_build_profile clang)" "Clang build profile"
assert_eq "2 1024 512 45 128 256" "$(rune_build_profile rust)" "Rust build profile"
assert_eq "2 2048 768 60 128 256" "$(rune_build_profile dotnet-aot)" ".NET AOT build profile"
assert_eq "1 512 256 20 128 256" "$(rune_build_profile python)" "MicroPython build profile"
assert_eq "1 512 256 20 128 256" "$(rune_build_profile ruby)" "mruby build profile"

snapshot="$repo_root/firecracker/build-snapshot.sh"
builder="$repo_root/firecracker/run-build-vm.sh"
assert_eq "1 192" "$(bash "$snapshot" --print-profile)" "Rune snapshot profile"
assert_eq "1 512 512 30 128 256" "$(bash "$builder" --print-profile scriptc)" "ScriptC build launcher profile"
assert_eq "2 1024 512 45 128 256" "$(bash "$builder" --print-profile rust)" "Rust build launcher profile"
assert_eq "2 2048 768 60 128 256" "$(bash "$builder" --print-profile dotnet-aot)" ".NET AOT build launcher profile"

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

grep -q 'timeout .*wall_seconds' "$builder" || { echo "build VM launcher must enforce host wall time" >&2; exit 1; }
grep -q 'truncate -s .*disk_mib.*scratch' "$builder" || { echo "build VM launcher must bound scratch disk" >&2; exit 1; }
grep -q 'rune.language=' "$builder" || { echo "build VM must receive language" >&2; exit 1; }
grep -q 'drive_id.*input' "$builder" && grep -q 'is_read_only.*true' "$builder" || { echo "build input must be read-only" >&2; exit 1; }
grep -q 'size <= 16777216' "$builder" || { echo "artifact must be capped at 16 MiB" >&2; exit 1; }
grep -q 'sha256sum' "$builder" || { echo "artifact store must be content-addressed" >&2; exit 1; }

dockerfile="$repo_root/firecracker/images/Dockerfile.build"
build_guest="$repo_root/native/Rune.Firecracker.BuildGuest/src/main.rs"
invocation_dockerfile="$repo_root/firecracker/images/Dockerfile.invocation"
scriptc_builder="$repo_root/firecracker/build-tools/scriptc.sh"
grep -q 'dotnet publish.*PublishAot=true' "$dockerfile" || { echo ".NET build image must prewarm Native AOT assets before network is removed" >&2; exit 1; }
grep -q 'NUGET_PACKAGES=/opt/rune/nuget' "$dockerfile" || { echo ".NET build image must expose its prewarmed packages outside root home" >&2; exit 1; }
grep -q 'NUGET_PACKAGES.*opt/rune/nuget' "$build_guest" || { echo "build guest must use the prewarmed NuGet cache" >&2; exit 1; }
grep -q 'scriptc cache warm dynamic' "$dockerfile" || { echo "ScriptC dynamic cache must be prewarmed" >&2; exit 1; }
grep -q 'rune-build-scriptc' "$build_guest" || { echo "JS/TS builds must use the ScriptC policy wrapper" >&2; exit 1; }
grep -q 'scriptc build "$source" -o "$artifact"' "$scriptc_builder" || { echo "ScriptC wrapper must try static compilation first" >&2; exit 1; }
grep -q 'scriptc coverage "$source" --dynamic' "$scriptc_builder" || { echo "ScriptC wrapper must validate dynamic fallback" >&2; exit 1; }
grep -q 'scriptc build "$source" --dynamic -o "$artifact"' "$scriptc_builder" || { echo "ScriptC wrapper must use dynamic mode only as fallback" >&2; exit 1; }
grep -q 'MICROPY_PERSISTENT_CODE_LOAD' "$dockerfile" || { echo "MicroPython embed must load precompiled bytecode" >&2; exit 1; }
grep -q 'rune-build-python' "$build_guest" || { echo "Python builds must use the executable MicroPython packager" >&2; exit 1; }
grep -q 'rune-build-ruby' "$build_guest" || { echo "Ruby builds must use the executable mruby packager" >&2; exit 1; }
if grep -Eq 'python3|ruby|rune-runtime|RUNTIME' "$invocation_dockerfile"; then
  echo "invocation image must be language-agnostic" >&2
  exit 1
fi

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
