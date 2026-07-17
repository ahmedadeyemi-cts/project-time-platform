#!/usr/bin/env bash

set -Eeuo pipefail

BASE_REF="${1:-source/module-059-restored-on-current-live-20260717}"
MODULE_A_DIR="${2:-}"
MODULE_B_DIR="${3:-}"

usage() {
  echo "Usage:"
  echo "  $0 BASE_REF MODULE_A_DIRECTORY MODULE_B_DIRECTORY"
  echo
  echo "Example:"
  echo "  $0 source/module-059-restored-on-current-live-20260717 \\"
  echo "    \$HOME/project-time-platform-module-060-contracts \\"
  echo "    \$HOME/project-time-platform-module-061-new-feature"
}

[[ -n "$MODULE_A_DIR" && -n "$MODULE_B_DIR" ]] || {
  usage
  exit 2
}

[[ -d "$MODULE_A_DIR/.git" ]] || {
  echo "RESULT=FAILED"
  echo "REASON=MODULE_A_NOT_A_GIT_REPOSITORY"
  exit 1
}

[[ -d "$MODULE_B_DIR/.git" ]] || {
  echo "RESULT=FAILED"
  echo "REASON=MODULE_B_NOT_A_GIT_REPOSITORY"
  exit 1
}

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

A_FILES="$TMP_DIR/module-a-files.txt"
B_FILES="$TMP_DIR/module-b-files.txt"
OVERLAP="$TMP_DIR/overlap.txt"

git -C "$MODULE_A_DIR" fetch origin --quiet
git -C "$MODULE_B_DIR" fetch origin --quiet

git -C "$MODULE_A_DIR" rev-parse --verify "$BASE_REF^{commit}" >/dev/null
git -C "$MODULE_B_DIR" rev-parse --verify "$BASE_REF^{commit}" >/dev/null

git -C "$MODULE_A_DIR" diff --name-only "$BASE_REF"...HEAD | sort -u > "$A_FILES"
git -C "$MODULE_B_DIR" diff --name-only "$BASE_REF"...HEAD | sort -u > "$B_FILES"

comm -12 "$A_FILES" "$B_FILES" > "$OVERLAP"

echo "BASE_REF=$BASE_REF"
echo "MODULE_A_DIR=$MODULE_A_DIR"
echo "MODULE_A_BRANCH=$(git -C "$MODULE_A_DIR" branch --show-current)"
echo "MODULE_A_HEAD=$(git -C "$MODULE_A_DIR" rev-parse HEAD)"
echo "MODULE_A_CHANGED_FILE_COUNT=$(wc -l < "$A_FILES" | tr -d ' ')"
echo "MODULE_B_DIR=$MODULE_B_DIR"
echo "MODULE_B_BRANCH=$(git -C "$MODULE_B_DIR" branch --show-current)"
echo "MODULE_B_HEAD=$(git -C "$MODULE_B_DIR" rev-parse HEAD)"
echo "MODULE_B_CHANGED_FILE_COUNT=$(wc -l < "$B_FILES" | tr -d ' ')"
echo "OVERLAPPING_FILE_COUNT=$(wc -l < "$OVERLAP" | tr -d ' ')"

if [[ -s "$OVERLAP" ]]; then
  echo "POTENTIAL_CONFLICT=YES"
  echo "OVERLAPPING_FILES_BEGIN"
  cat "$OVERLAP"
  echo "OVERLAPPING_FILES_END"
else
  echo "POTENTIAL_CONFLICT=NO"
fi

echo "READ_ONLY_CHECK=YES"
echo "GIT_CHANGED=NO"
echo "GITHUB_CHANGED=NO"
echo "AZURE_CHANGED=NO"
