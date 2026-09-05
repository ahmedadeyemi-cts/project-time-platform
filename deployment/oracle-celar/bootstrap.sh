#!/usr/bin/env bash
set -Eeuo pipefail

REPOSITORY_URL='https://github.com/ahmedadeyemi-cts/project-time-platform.git'
SOURCE_DIR='/opt/celar-ai/source'
STATE_DIR='/var/lib/celar-ai'

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
bash "$SOURCE_DIR/deployment/oracle-celar/deploy.sh"

TARGET_TREE="$(git -C "$SOURCE_DIR" rev-parse "$TARGET_COMMIT:deployment/oracle-celar")"
printf '%s\n' "$TARGET_COMMIT" > "$STATE_DIR/gitops-applied-commit"
printf '%s\n' "$TARGET_TREE" > "$STATE_DIR/gitops-applied-tree"
chmod 0644 "$STATE_DIR/gitops-applied-commit" "$STATE_DIR/gitops-applied-tree"

systemctl enable --now celar-gitops.timer
systemctl start celar-gitops.service

echo "CELAR_ORACLE_BOOTSTRAP=PASS"
echo "APPLIED_COMMIT=$TARGET_COMMIT"
