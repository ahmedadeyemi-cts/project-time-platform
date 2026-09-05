#!/usr/bin/env bash
set -Eeuo pipefail

REPOSITORY_URL='https://github.com/ahmedadeyemi-cts/project-time-platform.git'
SOURCE_DIR='/opt/celar-ai/source'
STATE_DIR='/var/lib/celar-ai'
GITOPS_DRAIN_TIMEOUT_SECONDS=600

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

[[ "$(id -u)" -eq 0 ]] || fail 'Run bootstrap.sh with sudo/root.'
[[ -r /etc/os-release ]] || fail '/etc/os-release is missing.'
# shellcheck disable=SC1091
. /etc/os-release
[[ "${ID:-}" == ubuntu && "${VERSION_ID:-}" == 24.04* ]] || fail 'Ubuntu 24.04 LTS is required.'
[[ "$(uname -m)" == aarch64 ]] || fail 'ARM64/aarch64 is required for this Oracle runtime profile.'

export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y ca-certificates curl git jq

install -d -m 0755 /opt/celar-ai
install -d -m 0755 "$STATE_DIR"

if [[ ! -d "$SOURCE_DIR/.git" ]]; then
  rm -rf "$SOURCE_DIR"
  git clone "$REPOSITORY_URL" "$SOURCE_DIR"
else
  git -C "$SOURCE_DIR" remote set-url origin "$REPOSITORY_URL"
fi

git -C "$SOURCE_DIR" fetch --no-tags --prune origin main
TARGET_COMMIT="$(git -C "$SOURCE_DIR" rev-parse origin/main)"
[[ "$TARGET_COMMIT" =~ ^[0-9a-f]{40}$ ]] || fail 'Could not resolve origin/main.'

git -C "$SOURCE_DIR" checkout --detach "$TARGET_COMMIT"

test -x "$SOURCE_DIR/deployment/oracle-celar/deploy.sh" || fail 'Oracle Celar deployment package is not present on main.'

# A retry may inherit an active timer from a prior successful/partial runtime.
# Remember that state before stopping future triggers. If this bootstrap fails,
# restore only a timer that was active on entry so recovery automation is not
# silently disabled by a failed retry.
GITOPS_TIMER_WAS_ACTIVE=false
if systemctl is-active --quiet celar-gitops.timer; then
  GITOPS_TIMER_WAS_ACTIVE=true
fi

restore_timer_on_failure() {
  local status=$?
  trap - EXIT INT TERM
  if [[ "$status" -ne 0 && "$GITOPS_TIMER_WAS_ACTIVE" == true ]]; then
    systemctl start celar-gitops.timer >/dev/null 2>&1 || true
  fi
  exit "$status"
}
trap restore_timer_on_failure EXIT INT TERM

systemctl stop celar-gitops.timer >/dev/null 2>&1 || true

# Let an already-running reconciliation leave the shared runtime mutation path,
# but never wait forever. A stuck fetch/package/model operation must surface as
# a bounded, diagnosable bootstrap failure.
DRAIN_DEADLINE=$((SECONDS + GITOPS_DRAIN_TIMEOUT_SECONDS))
while systemctl is-active --quiet celar-gitops.service; do
  if (( SECONDS >= DRAIN_DEADLINE )); then
    systemctl --no-pager --full status celar-gitops.service >&2 || true
    journalctl -u celar-gitops.service -n 80 --no-pager >&2 || true
    fail "Timed out after ${GITOPS_DRAIN_TIMEOUT_SECONDS}s waiting for active GitOps reconciliation to drain."
  fi
  sleep 2
done

bash "$SOURCE_DIR/deployment/oracle-celar/deploy.sh"

TARGET_TREE="$(git -C "$SOURCE_DIR" rev-parse "$TARGET_COMMIT:deployment/oracle-celar")"
printf '%s\n' "$TARGET_COMMIT" > "$STATE_DIR/gitops-applied-commit"
printf '%s\n' "$TARGET_TREE" > "$STATE_DIR/gitops-applied-tree"
chmod 0644 "$STATE_DIR/gitops-applied-commit" "$STATE_DIR/gitops-applied-tree"

# Start polling only after a successful deployment has an applied-state marker.
# The immediate service invocation should therefore converge to a no-op unless
# main advanced while the bootstrap was running.
systemctl enable --now celar-gitops.timer
systemctl start celar-gitops.service

trap - EXIT INT TERM

echo "CELAR_ORACLE_BOOTSTRAP=PASS"
echo "APPLIED_COMMIT=$TARGET_COMMIT"
