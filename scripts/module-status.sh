#!/usr/bin/env bash

set +e
set +u
set +o pipefail 2>/dev/null || true

ROOT="${HOME}"
FOUND=0

echo "================================================================================================================"
printf '%-52s %-48s %-12s %-10s %-10s\n' \
  "WORKSPACE" "BRANCH" "COMMIT" "STATE" "TRACKING"
echo "================================================================================================================"

for directory in "$ROOT"/project-time-platform-module-*; do
  [[ -d "$directory/.git" ]] || continue

  case "$(basename "$directory")" in
    *.previous.*)
      continue
      ;;
  esac

  FOUND=1
  branch="$(git -C "$directory" branch --show-current 2>/dev/null)"
  commit="$(git -C "$directory" rev-parse --short=12 HEAD 2>/dev/null)"
  changes="$(git -C "$directory" status --porcelain 2>/dev/null)"
  upstream="$(git -C "$directory" rev-parse --abbrev-ref '@{upstream}' 2>/dev/null)"

  [[ -n "$changes" ]] && state="CHANGED" || state="CLEAN"
  [[ -n "$upstream" ]] && tracking="YES" || tracking="NO"

  printf '%-52s %-48s %-12s %-10s %-10s\n' \
    "$(basename "$directory")" \
    "${branch:-DETACHED}" \
    "${commit:-UNKNOWN}" \
    "$state" \
    "$tracking"
done

echo "================================================================================================================"

[[ "$FOUND" -eq 1 ]] || echo "NO_ACTIVE_MODULE_WORKSPACES_FOUND"

echo
echo "ARCHIVED_PREVIOUS_DIRECTORIES_EXCLUDED=YES"
echo "READ_ONLY_SCAN=YES"
echo "GIT_CHANGED=NO"
echo "GITHUB_CHANGED=NO"
echo "AZURE_CHANGED=NO"
