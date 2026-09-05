#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
MANIFEST="$ROOT/release.json"
[[ -s "$MANIFEST" ]] || MANIFEST='/opt/celar-ai/deploy/release.json'
BACKUP_ROOT='/var/backups/celar-ai'
RESTIC_ENV='/etc/celar-ai/backup.env'

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

[[ "$(id -u)" -eq 0 ]] || fail 'backup.sh requires root.'
command -v jq >/dev/null 2>&1 || fail 'jq is required.'
command -v tar >/dev/null 2>&1 || fail 'tar is required.'
command -v zstd >/dev/null 2>&1 || fail 'zstd is required.'

RETENTION_DAYS="$(jq -r '.localBackupRetentionDays' "$MANIFEST")"
KEEP_DAILY="$(jq -r '.resticKeepDaily' "$MANIFEST")"
KEEP_WEEKLY="$(jq -r '.resticKeepWeekly' "$MANIFEST")"
KEEP_MONTHLY="$(jq -r '.resticKeepMonthly' "$MANIFEST")"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
ARCHIVE="$BACKUP_ROOT/celar-state-$STAMP.tar.zst"
LIST_FILE="$(mktemp)"
trap 'rm -f "$LIST_FILE"' EXIT

install -d -m 0700 "$BACKUP_ROOT"

add_path() {
  local path="$1"
  [[ -e "$path" ]] && printf '%s\n' "${path#/}" >> "$LIST_FILE"
}

add_path /etc/celar-ai
add_path /etc/caddy
add_path /etc/clamav/clamd.conf
add_path /etc/iptables/rules.v4
add_path /etc/iptables/rules.v6
add_path /etc/systemd/system/ollama.service.d
add_path /var/lib/celar-ai
add_path /var/lib/caddy
add_path /root/celar-firewall-backup

sort -u -o "$LIST_FILE" "$LIST_FILE"
[[ -s "$LIST_FILE" ]] || fail 'No backup paths were found.'

# Local quick-rollback archive. Deliberately exclude reproducible model/signature data
# and credential source files. External Restic encryption is the disaster-recovery copy.
tar \
  --create \
  --directory=/ \
  --files-from="$LIST_FILE" \
  --exclude='etc/celar-ai/backup.env' \
  --exclude='etc/celar-ai/gateway/runtime-token' \
  --exclude='var/lib/celar-ai/ollama-models' \
  --exclude='var/lib/ollama' \
  --exclude='var/lib/clamav' \
  --warning=no-file-changed \
  --ignore-failed-read \
  | zstd -T0 -8 -q -o "$ARCHIVE"
chmod 0600 "$ARCHIVE"

find "$BACKUP_ROOT" -maxdepth 1 -type f -name 'celar-state-*.tar.zst' -mtime "+$RETENTION_DAYS" -delete

RESTIC_STATUS='NOT_CONFIGURED'
if [[ -s "$RESTIC_ENV" ]]; then
  chmod 0600 "$RESTIC_ENV"
  # shellcheck disable=SC1090
  . "$RESTIC_ENV"
  : "${RESTIC_REPOSITORY:?RESTIC_REPOSITORY is required in backup.env}"
  : "${RESTIC_PASSWORD:?RESTIC_PASSWORD is required in backup.env}"
  export RESTIC_REPOSITORY RESTIC_PASSWORD
  export AWS_ACCESS_KEY_ID="${AWS_ACCESS_KEY_ID:-}"
  export AWS_SECRET_ACCESS_KEY="${AWS_SECRET_ACCESS_KEY:-}"
  export AWS_DEFAULT_REGION="${AWS_DEFAULT_REGION:-}"

  if ! restic snapshots >/dev/null 2>&1; then
    restic init
  fi

  mapfile -t RESTIC_PATHS < <(sed 's#^#/#' "$LIST_FILE")
  restic backup \
    --tag celar-oracle \
    --exclude='/etc/celar-ai/backup.env' \
    --exclude='/etc/celar-ai/gateway/runtime-token' \
    --exclude='/var/lib/celar-ai/ollama-models' \
    --exclude='/var/lib/ollama' \
    --exclude='/var/lib/clamav' \
    "${RESTIC_PATHS[@]}"
  restic forget \
    --tag celar-oracle \
    --keep-daily "$KEEP_DAILY" \
    --keep-weekly "$KEEP_WEEKLY" \
    --keep-monthly "$KEEP_MONTHLY" \
    --prune
  RESTIC_STATUS='PASS'
fi

echo "CELAR_LOCAL_BACKUP=PASS ARCHIVE=$ARCHIVE"
echo "CELAR_EXTERNAL_RESTIC_BACKUP=$RESTIC_STATUS"
