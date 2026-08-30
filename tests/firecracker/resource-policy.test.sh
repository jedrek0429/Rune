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

assert_eq "1 512 512 30 128 256" "$(rune_build_profile scriptc)" "ScriptC build profile"
assert_eq "1 512 512 20 128 256" "$(rune_build_profile clang)" "Clang build profile"
assert_eq "2 1024 512 45 128 256" "$(rune_build_profile rust)" "Rust build profile"
assert_eq "2 2048 768 60 128 256" "$(rune_build_profile dotnet-aot)" ".NET AOT build profile"
assert_eq "1 512 256 20 128 256" "$(rune_build_profile python)" "Python build profile"
assert_eq "1 512 256 20 128 256" "$(rune_build_profile ruby)" "Ruby build profile"

assert_eq "1 192" "$($repo_root/firecracker/build-snapshot.sh --print-profile native)" "native snapshot profile"
assert_eq "1 256" "$($repo_root/firecracker/build-snapshot.sh --print-profile python)" "python snapshot profile"
assert_eq "1 256" "$($repo_root/firecracker/build-snapshot.sh --print-profile ruby)" "ruby snapshot profile"

if "$repo_root/firecracker/build-snapshot.sh" --print-profile javascript >/dev/null 2>&1; then
  echo "javascript must not have a language-specific invocation snapshot" >&2
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

echo "resource policy tests passed"
