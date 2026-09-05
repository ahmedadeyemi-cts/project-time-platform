#!/usr/bin/env bash
set -Eeuo pipefail

RESTIC_ENV='/etc/celar-ai/backup.env'
RECOVERY_ROOT='/var/lib/celar-ai/recovery'
SNAPSHOT="${1:-latest}"
BACKUP_TAG='celar-oracle'

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

[[ "$(id -u)" -eq 0 ]] || fail 'restore.sh requires root.'
[[ -s "$RESTIC_ENV" ]] || fail '/etc/celar-ai/backup.env is not configured.'
command -v restic >/dev/null 2>&1 || fail 'restic is not installed.'

chmod 0600 "$RESTIC_ENV"
# shellcheck disable=SC1090
. "$RESTIC_ENV"
: "${RESTIC_REPOSITORY:?RESTIC_REPOSITORY is required in backup.env}"
: "${RESTIC_PASSWORD:?RESTIC_PASSWORD is required in backup.env}"
export RESTIC_REPOSITORY RESTIC_PASSWORD
export AWS_ACCESS_KEY_ID="${AWS_ACCESS_KEY_ID:-}"
export AWS_SECRET_ACCESS_KEY="${AWS_SECRET_ACCESS_KEY:-}"
export AWS_DEFAULT_REGION="${AWS_DEFAULT_REGION:-}"

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
TARGET="$RECOVERY_ROOT/$STAMP"
install -d -m 0700 "$TARGET"

# This repository may be shared with other backup sets. Scope both discovery and
# restore selection to the Celar tag so `latest` can never resolve to another
# host/application's newest snapshot.
restic snapshots --tag "$BACKUP_TAG"
restic restore "$SNAPSHOT" --tag "$BACKUP_TAG" --target "$TARGET"

cat > "$TARGET/RESTORE-NOTICE.txt" <<EOF
Celar AI recovery snapshot staged at $STAMP.

Nothing in this recovery tree has been copied over the live operating system.
GitHub remains the canonical source for configuration. Review dynamic state here
before selectively restoring it. Reproducible Ollama model blobs and ClamAV
signatures are intentionally not part of the disaster-recovery snapshot.
EOF
chmod 0600 "$TARGET/RESTORE-NOTICE.txt"

echo "CELAR_RESTORE_STAGED=PASS SNAPSHOT=$SNAPSHOT TAG=$BACKUP_TAG TARGET=$TARGET"
