#!/usr/bin/env bash
# PostToolUse hook (Edit|Write): compile-check the project after a source
# file changes. Skips silently when the edited file cannot affect the build
# or when the dotnet SDK is not available (e.g. outside the cloud container).
set -uo pipefail

file_path="$(jq -r '.tool_input.file_path // empty')"
[ -z "$file_path" ] && exit 0

case "$file_path" in
  *.cs|*.csproj|*.resx|*.config|*.manifest) ;;
  *) exit 0 ;;
esac

command -v dotnet >/dev/null 2>&1 || exit 0

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
output="$(bash "$repo_root/scripts/linux-build.sh" 2>&1)" && exit 0

errors="$(printf '%s\n' "$output" | grep -E 'error [A-Z]+[0-9]+' | head -25)"
if [ -n "$errors" ]; then
  printf '%s\n' "$errors" >&2
else
  echo "Build failed (no compiler errors matched) — run 'bash scripts/linux-build.sh' for full output." >&2
fi
exit 2
