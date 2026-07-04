#!/usr/bin/env bash
set -Eeuo pipefail

CONFIG="/opt/project-time-platform/config/backup-schedule.env"
PENDING="/opt/project-time-platform/backup-requests/pending"
STATE_DIR="/opt/project-time-platform/state"
STATE_FILE="$STATE_DIR/backup-scheduler-last-run"

mkdir -p "$PENDING" "$STATE_DIR"

if [ ! -f "$CONFIG" ]; then
  exit 0
fi

set -a
source "$CONFIG"
set +a

if [ "${PROJECTPULSE_BACKUP_SCHEDULE_ENABLED:-false}" != "true" ]; then
  exit 0
fi

MODE="${PROJECTPULSE_BACKUP_SCHEDULE_MODE:-daily}"
TIME_UTC="${PROJECTPULSE_BACKUP_SCHEDULE_TIME_UTC:-06:00}"
NOW_TIME="$(date -u +%H:%M)"

if [[ "$NOW_TIME" < "$TIME_UTC" ]]; then
  exit 0
fi

PERIOD=""
case "$MODE" in
  weekly)
    WEEKLY_DAY="${PROJECTPULSE_BACKUP_SCHEDULE_WEEKLY_DAY_UTC:-7}"
    TODAY="$(date -u +%u)"
    if [ "$TODAY" != "$WEEKLY_DAY" ]; then
      exit 0
    fi
    PERIOD="weekly-$(date -u +%G-W%V)"
    ;;
  monthly)
    MONTHLY_DAY="${PROJECTPULSE_BACKUP_SCHEDULE_MONTHLY_DAY_UTC:-1}"
    TODAY="$(date -u +%-d)"
    if [ "$TODAY" != "$MONTHLY_DAY" ]; then
      exit 0
    fi
    PERIOD="monthly-$(date -u +%Y-%m)"
    ;;
  *)
    PERIOD="daily-$(date -u +%Y-%m-%d)"
    ;;
esac

if [ -f "$STATE_FILE" ] && grep -qx "$PERIOD" "$STATE_FILE"; then
  exit 0
fi

REQUEST_ID="scheduled-$(date -u +%Y%m%dT%H%M%SZ)"
REQUEST_FILE="$PENDING/$REQUEST_ID.json"

UPLOAD_SFTP="${PROJECTPULSE_BACKUP_SCHEDULE_UPLOAD_TO_SFTP:-false}"
UPLOAD_AZURE="${PROJECTPULSE_BACKUP_SCHEDULE_UPLOAD_TO_AZURE:-false}"

cat > "$REQUEST_FILE" <<EOF
{
  "requestId": "$REQUEST_ID",
  "requestedAt": "$(date -u --iso-8601=seconds)",
  "requestedByUserId": null,
  "requestedByEmail": "system-scheduler",
  "uploadToSftp": $UPLOAD_SFTP,
  "uploadToAzure": $UPLOAD_AZURE,
  "reason": "Scheduled $MODE Project Health Dashboard backup"
}
EOF

chmod 660 "$REQUEST_FILE"
echo "$PERIOD" > "$STATE_FILE"
chmod 644 "$STATE_FILE"
