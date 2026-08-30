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

assert_eq "1 192 32 3 32 128" "$(rune_invocation_profile native)" "native invocation profile"
assert_eq "1 256 32 3 32 128" "$(rune_invocation_profile python)" "python invocation profile"
assert_eq "1 256 32 3 32 128" "$(rune_invocation_profile ruby)" "ruby invocation profile"
assert_eq "1 512 512 60 128 256" "$(rune_build_profile scriptc)" "ScriptC build profile"
assert_eq "1 512 512 20 128 256" "$(rune_build_profile clang)" "Clang build profile"
assert_eq "2 1024 512 45 128 256" "$(rune_build_profile rust)" "Rust build profile"
assert_eq "2 2048 768 60 128 256" "$(rune_build_profile dotnet-aot)" ".NET AOT build profile"
assert_eq "1 512 256 20 128 256" "$(rune_build_profile python)" "Python build profile"
assert_eq "1 512 256 20 128 256" "$(rune_build_profile ruby)" "Ruby build profile"

snapshot="$repo_root/firecracker/build-snapshot.sh"
builder="$repo_root/firecracker/run-build-vm.sh"
assert_eq "1 192" "$(bash "$snapshot" --print-profile native)" "native snapshot profile"
assert_eq "1 256" "$(bash "$snapshot" --print-profile python)" "python snapshot profile"
assert_eq "1 256" "$(bash "$snapshot" --print-profile ruby)" "ruby snapshot profile"
assert_eq "1 512 512 60 128 256" "$(bash "$builder" --print-profile scriptc)" "ScriptC build launcher profile"
assert_eq "2 1024 512 45 128 256" "$(bash "$builder" --print-profile rust)" "Rust build launcher profile"
assert_eq "2 2048 768 60 128 256" "$(bash "$builder" --print-profile dotnet-aot)" ".NET AOT build launcher profile"

if bash "$snapshot" --print-profile javascript >/dev/null 2>&1; then
  echo "javascript must not have a language-specific invocation snapshot" >&2
  exit 1
fi
if bash "$builder" --print-profile node >/dev/null 2>&1; then
  echo "Node must not be a Rune build pool" >&2
  exit 1
fi
if rune_invocation_profile dotnet >/dev/null 2>&1; then
  echo "dotnet must not be a separate invocation runtime" >&2
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
grep -q 'dotnet publish.*PublishAot=true' "$dockerfile" || { echo ".NET build image must prewarm Native AOT assets before network is removed" >&2; exit 1; }
grep -q 'NUGET_PACKAGES=/opt/rune/nuget' "$dockerfile" || { echo ".NET build image must expose its prewarmed packages outside root home" >&2; exit 1; }
grep -q 'NUGET_PACKAGES.*opt/rune/nuget' "$build_guest" || { echo "build guest must use the prewarmed NuGet cache" >&2; exit 1; }
grep -q 'allow-scripts=scriptc' "$dockerfile" || { echo "ScriptC build image must run its cache-warming install hook" >&2; exit 1; }
grep -q 'SCRIPTC_CACHE_DIR=/opt/rune/scriptc-cache' "$dockerfile" || { echo "ScriptC build image must use an explicit compiler cache" >&2; exit 1; }
grep -q 'chown -R 1000:1000.*SCRIPTC_CACHE_DIR' "$dockerfile" || { echo "ScriptC cache must be owned by the unprivileged build user" >&2; exit 1; }
grep -q 'chmod 0700.*SCRIPTC_CACHE_DIR' "$dockerfile" || { echo "ScriptC cache must stay private" >&2; exit 1; }
grep -q 'SCRIPTC_CACHE_DIR.*opt/rune/scriptc-cache' "$build_guest" || { echo "build guest must reuse the prewarmed ScriptC cache" >&2; exit 1; }

e2e="$repo_root/tests/firecracker/e2e.sh"
[[ -x "$e2e" ]] || { echo "eight-language Firecracker e2e harness is missing" >&2; exit 1; }
for language in javascript typescript python ruby rust c cpp csharp; do
  grep -q "^  $language)" "$e2e" || { echo "e2e harness does not cover $language" >&2; exit 1; }
done

echo "resource policy tests passed"
