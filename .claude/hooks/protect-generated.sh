#!/usr/bin/env bash
# PreToolUse hook (Edit|Write): block edits under restored/generated paths.
# packages/ is restored from nuget.org and bin/ / obj/ are build output —
# any change there is lost on the next restore/build.
set -euo pipefail

file_path="$(jq -r '.tool_input.file_path // empty')"
[ -z "$file_path" ] && exit 0

case "$file_path" in
  */packages/*|*/bin/*|*/obj/*)
    echo "Blocked: '$file_path' is a restored/generated path (packages/, bin/, obj/ are recreated by restore/build). Edit the source files instead." >&2
    exit 2
    ;;
esac

exit 0
