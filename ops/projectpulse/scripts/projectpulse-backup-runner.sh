#!/usr/bin/env bash
set -Eeuo pipefail

REQ_ROOT="/opt/project-time-platform/backup-requests"
PENDING="$REQ_ROOT/pending"
PROCESSED="$REQ_ROOT/processed"
FAILED="$REQ_ROOT/failed"
RESULTS="/opt/project-time-platform/backups/results"

mkdir -p "$PENDING" "$PROCESSED" "$FAILED" "$RESULTS"

shopt -s nullglob

for REQUEST_FILE in "$PENDING"/*.json; do
  REQUEST_ID="$(basename "$REQUEST_FILE" .json)"
  OUTPUT_FILE="$RESULTS/$REQUEST_ID.output.log"
  RESULT_FILE="$RESULTS/$REQUEST_ID.result.json"

  UPLOAD_TO_SFTP="$(python3 -c 'import json,sys; data=json.load(open(sys.argv[1])); print("true" if data.get("uploadToSftp") else "false")' "$REQUEST_FILE")"
  UPLOAD_TO_AZURE="$(python3 -c 'import json,sys; data=json.load(open(sys.argv[1])); print("true" if data.get("uploadToAzure") else "false")' "$REQUEST_FILE")"

  ARGS=()

  if [ "$UPLOAD_TO_SFTP" = "true" ]; then
    ARGS+=(--upload-sftp)
  fi

  if [ "$UPLOAD_TO_AZURE" = "true" ]; then
    ARGS+=(--upload-azure)
  fi

  STARTED_AT="$(date -u --iso-8601=seconds)"

  set +e
  /usr/local/sbin/projectpulse-backup.sh "${ARGS[@]}" >"$OUTPUT_FILE" 2>&1
  EXIT_CODE=$?
  set -e

  COMPLETED_AT="$(date -u --iso-8601=seconds)"

  if [ "$EXIT_CODE" -eq 0 ]; then
    STATUS="completed"
    TARGET_DIR="$PROCESSED"
  else
    STATUS="failed"
    TARGET_DIR="$FAILED"
  fi

  python3 - "$REQUEST_FILE" "$RESULT_FILE" "$OUTPUT_FILE" "$STATUS" "$EXIT_CODE" "$STARTED_AT" "$COMPLETED_AT" <<'PY'
import json
import sys

request_file, result_file, output_file, status, exit_code, started_at, completed_at = sys.argv[1:]

with open(request_file, "r", encoding="utf-8") as handle:
    request = json.load(handle)

with open(output_file, "r", encoding="utf-8", errors="replace") as handle:
    output = handle.read()

payload = {
    "request": request,
    "status": status,
    "exitCode": int(exit_code),
    "startedAt": started_at,
    "completedAt": completed_at,
    "outputFile": output_file,
    "output": output[-12000:]
}

with open(result_file, "w", encoding="utf-8") as handle:
    json.dump(payload, handle, indent=2)
PY

  mv "$REQUEST_FILE" "$TARGET_DIR/$REQUEST_ID.json"
  API_SERVICE="projecttime-api.service"
  if systemctl list-unit-files projectpulse-api.service --no-legend 2>/dev/null | grep -q '^projectpulse-api.service'; then
    API_SERVICE="projectpulse-api.service"
  fi

  API_USER="$(systemctl show "$API_SERVICE" -p User --value)"
  if [ -z "$API_USER" ]; then
    API_USER="root"
  fi

  API_GROUP="$(id -gn "$API_USER" 2>/dev/null || echo "$API_USER")"

  chown root:"$API_GROUP" "$RESULT_FILE" "$OUTPUT_FILE" || true
  API_SERVICE="projecttime-api.service"
  if systemctl list-unit-files projectpulse-api.service --no-legend 2>/dev/null | grep -q '^projectpulse-api.service'; then
    API_SERVICE="projectpulse-api.service"
  fi

  API_USER="$(systemctl show "$API_SERVICE" -p User --value)"
  if [ -z "$API_USER" ]; then
    API_USER="root"
  fi

  API_GROUP="$(id -gn "$API_USER" 2>/dev/null || echo "$API_USER")"

  chown root:"$API_GROUP" "$RESULT_FILE" "$OUTPUT_FILE" || true
  chmod 640 "$RESULT_FILE" "$OUTPUT_FILE" || true
  chown root:"$API_GROUP" "$RESULTS" || true
  chmod 750 "$RESULTS" || true
  chown root:"$API_GROUP" "$RESULTS" || true
  chmod 750 "$RESULTS" || true

  NOTIFY_ENV="/opt/project-time-platform/config/backup-notifications.env"
  if [ -f "$NOTIFY_ENV" ]; then
    set -a
    source "$NOTIFY_ENV"
    set +a

    RECIPIENTS=""
    SUBJECT=""

    if [ "$STATUS" = "completed" ] && [ "${PROJECTPULSE_BACKUP_NOTIFY_ON_SUCCESS:-false}" = "true" ]; then
      RECIPIENTS="${PROJECTPULSE_BACKUP_SUCCESS_RECIPIENTS:-}"
      SUBJECT="Project Health Dashboard backup completed"
    fi

    if [ "$STATUS" = "failed" ] && [ "${PROJECTPULSE_BACKUP_NOTIFY_ON_FAILURE:-true}" = "true" ]; then
      RECIPIENTS="${PROJECTPULSE_BACKUP_FAILURE_RECIPIENTS:-}"
      SUBJECT="Project Health Dashboard backup failed"
    fi

    echo "Notification decision: status=$STATUS notify_success=${PROJECTPULSE_BACKUP_NOTIFY_ON_SUCCESS:-false} notify_failure=${PROJECTPULSE_BACKUP_NOTIFY_ON_FAILURE:-true} recipients=$RECIPIENTS" >> "$OUTPUT_FILE"

    if [ -z "$RECIPIENTS" ]; then
      echo "Email notification skipped: no recipients configured for status=$STATUS." >> "$OUTPUT_FILE"
    fi

    if [ -n "$RECIPIENTS" ]; then
      BODY_FILE="$(mktemp)"
      {
        echo "Project Health Dashboard backup status: $STATUS"
        echo "Request ID: $REQUEST_ID"
        echo "Started at: $STARTED_AT"
        echo "Completed at: $COMPLETED_AT"
        echo "Exit code: $EXIT_CODE"
        echo
        echo "Result file: $RESULT_FILE"
        echo "Output file: $OUTPUT_FILE"
        echo
        echo "Recent output:"
        tail -120 "$OUTPUT_FILE" || true
      } > "$BODY_FILE"

      if [ "${PROJECTPULSE_BACKUP_SMTP_ENABLED:-false}" = "true" ] && [ -x /usr/local/sbin/projectpulse-send-backup-email.py ]; then
        /usr/local/sbin/projectpulse-send-backup-email.py "$SUBJECT" "$RECIPIENTS" "${PROJECTPULSE_BACKUP_CC_RECIPIENTS:-}" "$BODY_FILE" >> "$OUTPUT_FILE" 2>&1 || {
          echo "SMTP email notification failed." >> "$OUTPUT_FILE"
        }
      elif command -v sendmail >/dev/null 2>&1; then
        {
          echo "To: $RECIPIENTS"
          if [ -n "${PROJECTPULSE_BACKUP_CC_RECIPIENTS:-}" ]; then
            echo "Cc: $PROJECTPULSE_BACKUP_CC_RECIPIENTS"
          fi
          echo "Subject: $SUBJECT"
          echo
          cat "$BODY_FILE"
        } | sendmail -t || true
      elif command -v mail >/dev/null 2>&1; then
        mail -s "$SUBJECT" "$RECIPIENTS" < "$BODY_FILE" || true
      else
        echo "Email notification skipped: SMTP disabled and sendmail/mail command not available." >> "$OUTPUT_FILE"
      fi

      rm -f "$BODY_FILE"
    fi
  fi
done
