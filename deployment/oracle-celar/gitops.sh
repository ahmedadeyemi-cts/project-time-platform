#!/usr/bin/env bash
set -Eeuo pipefail

REPOSITORY_URL='https://github.com/ahmedadeyemi-cts/project-time-platform.git'
SOURCE_DIR='/opt/celar-ai/source'
STATE_DIR='/var/lib/celar-ai'
RELEASE_ROOT='/opt/celar-ai/releases'
LOCK_FILE='/run/celar-gitops.lock'

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

[[ "$(id -u)" -eq 0 ]] || fail 'gitops.sh requires root.'
exec 9>"$LOCK_FILE"
flock -n 9 || exit 0

install -d -m 0755 /opt/celar-ai "$STATE_DIR" "$RELEASE_ROOT"

if [[ ! -d "$SOURCE_DIR/.git" ]]; then
  git clone "$REPOSITORY_URL" "$SOURCE_DIR"
else
  git -C "$SOURCE_DIR" remote set-url origin "$REPOSITORY_URL"
fi

git -C "$SOURCE_DIR" fetch --no-tags --prune origin main
TARGET_COMMIT="$(git -C "$SOURCE_DIR" rev-parse origin/main)"
TARGET_TREE="$(git -C "$SOURCE_DIR" rev-parse "$TARGET_COMMIT:deployment/oracle-celar")"
CURRENT_TREE="$(cat "$STATE_DIR/gitops-applied-tree" 2>/dev/null || true)"

if [[ "$TARGET_TREE" == "$CURRENT_TREE" ]]; then
  exit 0
fi

RELEASE_DIR="$RELEASE_ROOT/$TARGET_COMMIT"
if [[ -e "$RELEASE_DIR" ]]; then
  git -C "$SOURCE_DIR" worktree remove --force "$RELEASE_DIR" >/dev/null 2>&1 || rm -rf "$RELEASE_DIR"
fi

git -C "$SOURCE_DIR" worktree add --detach "$RELEASE_DIR" "$TARGET_COMMIT"
cleanup() {
  local status=$?
  git -C "$SOURCE_DIR" worktree remove --force "$RELEASE_DIR" >/dev/null 2>&1 || true
  exit "$status"
}
trap cleanup EXIT INT TERM

test -x "$RELEASE_DIR/deployment/oracle-celar/deploy.sh" || fail 'Target Oracle deployment script is missing or not executable.'
bash "$RELEASE_DIR/deployment/oracle-celar/deploy.sh"

printf '%s\n' "$TARGET_COMMIT" > "$STATE_DIR/gitops-applied-commit"
printf '%s\n' "$TARGET_TREE" > "$STATE_DIR/gitops-applied-tree"
chmod 0644 "$STATE_DIR/gitops-applied-commit" "$STATE_DIR/gitops-applied-tree"

# Keep a small audit trail without retaining entire worktrees.
printf '%s %s %s\n' "$(date -u +%FT%TZ)" "$TARGET_COMMIT" "$TARGET_TREE" >> "$STATE_DIR/gitops-history.log"
tail -n 100 "$STATE_DIR/gitops-history.log" > "$STATE_DIR/gitops-history.log.tmp"
mv "$STATE_DIR/gitops-history.log.tmp" "$STATE_DIR/gitops-history.log"
chmod 0644 "$STATE_DIR/gitops-history.log"

echo "CELAR_GITOPS_APPLY=PASS COMMIT=$TARGET_COMMIT TREE=$TARGET_TREE"
