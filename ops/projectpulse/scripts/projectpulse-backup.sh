#!/usr/bin/env bash
set -Eeuo pipefail

UPLOAD_TO_SFTP=false
UPLOAD_TO_AZURE=false

for arg in "$@"; do
  case "$arg" in
    --upload|--upload-sftp)
      UPLOAD_TO_SFTP=true
      ;;
    --upload-azure)
      UPLOAD_TO_AZURE=true
      ;;
  esac
done

BACKUP_ROOT="/opt/project-time-platform/backups"
APP_DIR="/opt/project-time-platform/app/project-time-platform"
CONFIG_DIR="/opt/project-time-platform/config"
POSTGRES_ENV="/opt/project-time-platform/config/postgres.env"
SFTP_ENV="/opt/project-time-platform/config/backup-sftp.env"
AZURE_ENV="/opt/project-time-platform/config/backup-azure.env"

RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)"
BACKUP_NAME_TIMEZONE="${PROJECTPULSE_BACKUP_NAME_TIMEZONE:-America/Los_Angeles}"
BACKUP_NAME_TIMEZONE_LABEL="${PROJECTPULSE_BACKUP_NAME_TIMEZONE_LABEL:-PST}"
BACKUP_LABEL="$(TZ="$BACKUP_NAME_TIMEZONE" date "+Backup_%Y_%m_%d_%I:%M:%S%p${BACKUP_NAME_TIMEZONE_LABEL}")"
RUN_DIR="$BACKUP_ROOT/$BACKUP_LABEL"
BUNDLE="$BACKUP_ROOT/$BACKUP_LABEL.tgz"
MANIFEST="$RUN_DIR/manifest.txt"

mkdir -p "$RUN_DIR"
chmod 750 "$BACKUP_ROOT" "$RUN_DIR"

if [ ! -f "$POSTGRES_ENV" ]; then
  echo "ERROR: PostgreSQL environment file not found: $POSTGRES_ENV"
  exit 10
fi

set -a
source "$POSTGRES_ENV"
set +a

: "${PTP_DB_HOST:?Missing PTP_DB_HOST}"
: "${PTP_DB_PORT:=5432}"
: "${PTP_DB_NAME:?Missing PTP_DB_NAME}"
: "${PTP_DB_USER:?Missing PTP_DB_USER}"
: "${PTP_DB_PASSWORD:?Missing PTP_DB_PASSWORD}"

DB_DUMP="$RUN_DIR/$BACKUP_LABEL-database.dump"
CONFIG_ARCHIVE="$RUN_DIR/$BACKUP_LABEL-config.tgz"
APP_ARCHIVE="$RUN_DIR/$BACKUP_LABEL-app-snapshot.tgz"
CHECKSUM_FILE="$RUN_DIR/sha256sums.txt"

{
  echo "ProjectPulse Backup Manifest"
  echo "Run ID: $RUN_ID"
  echo "Backup name: $BACKUP_LABEL"
  echo "Created UTC: $(date -u --iso-8601=seconds)"
  echo "Host: $(hostname -f 2>/dev/null || hostname)"
  echo "Application directory: $APP_DIR"
  echo "Database: $PTP_DB_NAME"
  echo

  if [ -d "$APP_DIR/.git" ]; then
    echo "Git branch: $(git -C "$APP_DIR" rev-parse --abbrev-ref HEAD 2>/dev/null || true)"
    echo "Git commit: $(git -C "$APP_DIR" rev-parse HEAD 2>/dev/null || true)"
    echo "Git status:"
    git -C "$APP_DIR" status --short 2>/dev/null || true
  fi
} > "$MANIFEST"

echo "Creating PostgreSQL database backup..."
PGPASSWORD="$PTP_DB_PASSWORD" pg_dump \
  -h "$PTP_DB_HOST" \
  -p "$PTP_DB_PORT" \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -Fc \
  -f "$DB_DUMP"

echo "Creating configuration archive..."
tar --ignore-failed-read -czf "$CONFIG_ARCHIVE" \
  "$CONFIG_DIR" \
  /etc/systemd/system/projecttime-api.service \
  /etc/systemd/system/projecttime-api.service.d \
  /etc/systemd/system/projecttime-frontend-public.service \
  /etc/systemd/system/projecttime-frontend-public.service.d \
  /etc/systemd/system/projectpulse-readiness-export.service \
  /etc/systemd/system/projectpulse-readiness-export.timer \
  /etc/systemd/system/projectpulse-backup-runner.service \
  /etc/systemd/system/projectpulse-backup-runner.timer \
  /etc/nginx/conf.d/projectpulse.conf \
  /usr/local/sbin/projectpulse-backup.sh \
  /usr/local/sbin/projectpulse-backup-runner.sh \
  /usr/local/sbin/projectpulse-readiness-export.sh \
  2>/dev/null || true

echo "Creating application source snapshot..."
tar \
  --exclude='node_modules' \
  --exclude='bin' \
  --exclude='obj' \
  --exclude='dist' \
  --exclude='.git/objects' \
  -czf "$APP_ARCHIVE" \
  -C /opt/project-time-platform/app \
  project-time-platform

echo "Creating checksums..."
sha256sum "$DB_DUMP" "$CONFIG_ARCHIVE" "$APP_ARCHIVE" "$MANIFEST" > "$CHECKSUM_FILE"

echo "Creating final backup bundle..."
tar -C "$BACKUP_ROOT" -czf "$BUNDLE" "$BACKUP_LABEL"
sha256sum "$BUNDLE" > "$BUNDLE.sha256"

SFTP_UPLOAD_STATUS="not_requested"
AZURE_UPLOAD_STATUS="not_requested"

if [ "$UPLOAD_TO_SFTP" = true ]; then
  if [ ! -f "$SFTP_ENV" ]; then
    echo "ERROR: SFTP upload requested but config file not found: $SFTP_ENV"
    exit 20
  fi

  set -a
  source "$SFTP_ENV"
  set +a

  : "${PROJECTPULSE_BACKUP_SFTP_HOST:?Missing PROJECTPULSE_BACKUP_SFTP_HOST}"
  : "${PROJECTPULSE_BACKUP_SFTP_PORT:=22}"
  : "${PROJECTPULSE_BACKUP_SFTP_USER:?Missing PROJECTPULSE_BACKUP_SFTP_USER}"
  : "${PROJECTPULSE_BACKUP_SFTP_REMOTE_PATH:?Missing PROJECTPULSE_BACKUP_SFTP_REMOTE_PATH}"
  : "${PROJECTPULSE_BACKUP_SFTP_AUTH_MODE:=private_key}"

  SFTP_BATCH="$(mktemp)"
  {
    echo "cd $PROJECTPULSE_BACKUP_SFTP_REMOTE_PATH"
    echo "put $BUNDLE"
    echo "put $BUNDLE.sha256"
  } > "$SFTP_BATCH"

  echo "Uploading backup bundle to SFTP using auth mode: $PROJECTPULSE_BACKUP_SFTP_AUTH_MODE"

  if [ "$PROJECTPULSE_BACKUP_SFTP_AUTH_MODE" = "password" ]; then
    if ! command -v sshpass >/dev/null 2>&1; then
      echo "ERROR: Password-based SFTP requires sshpass. Install sshpass or use private-key SFTP."
      rm -f "$SFTP_BATCH"
      exit 22
    fi

    : "${PROJECTPULSE_BACKUP_SFTP_PASSWORD:?Missing PROJECTPULSE_BACKUP_SFTP_PASSWORD}"

    SSHPASS="$PROJECTPULSE_BACKUP_SFTP_PASSWORD" sshpass -e sftp \
      -b "$SFTP_BATCH" \
      -P "$PROJECTPULSE_BACKUP_SFTP_PORT" \
      -oBatchMode=no \
      -oStrictHostKeyChecking=accept-new \
      "$PROJECTPULSE_BACKUP_SFTP_USER@$PROJECTPULSE_BACKUP_SFTP_HOST"
  else
    : "${PROJECTPULSE_BACKUP_SFTP_KEY_PATH:?Missing PROJECTPULSE_BACKUP_SFTP_KEY_PATH}"

    if [ ! -f "$PROJECTPULSE_BACKUP_SFTP_KEY_PATH" ]; then
      echo "ERROR: SFTP key does not exist: $PROJECTPULSE_BACKUP_SFTP_KEY_PATH"
      rm -f "$SFTP_BATCH"
      exit 21
    fi

    sftp \
      -b "$SFTP_BATCH" \
      -i "$PROJECTPULSE_BACKUP_SFTP_KEY_PATH" \
      -P "$PROJECTPULSE_BACKUP_SFTP_PORT" \
      -oBatchMode=yes \
      -oStrictHostKeyChecking=accept-new \
      "$PROJECTPULSE_BACKUP_SFTP_USER@$PROJECTPULSE_BACKUP_SFTP_HOST"
  fi

  rm -f "$SFTP_BATCH"
  SFTP_UPLOAD_STATUS="uploaded"
fi

if [ "$UPLOAD_TO_AZURE" = true ]; then
  if [ ! -f "$AZURE_ENV" ]; then
    echo "ERROR: Azure upload requested but config file not found: $AZURE_ENV"
    exit 30
  fi

  set -a
  source "$AZURE_ENV"
  set +a

  : "${PROJECTPULSE_BACKUP_AZURE_CONTAINER_SAS_URL:?Missing PROJECTPULSE_BACKUP_AZURE_CONTAINER_SAS_URL}"
  : "${PROJECTPULSE_BACKUP_AZURE_BLOB_PREFIX:=projectpulse-backups}"

  if ! command -v azcopy >/dev/null 2>&1; then
    echo "ERROR: Azure Blob upload requires azcopy."
    exit 31
  fi

  BASE_URL="${PROJECTPULSE_BACKUP_AZURE_CONTAINER_SAS_URL%%\?*}"
  SAS_QUERY="${PROJECTPULSE_BACKUP_AZURE_CONTAINER_SAS_URL#*\?}"

  if [ "$BASE_URL" = "$SAS_QUERY" ]; then
    echo "ERROR: Azure SAS URL must include a SAS query string."
    exit 32
  fi

  PREFIX="${PROJECTPULSE_BACKUP_AZURE_BLOB_PREFIX#/}"
  PREFIX="${PREFIX%/}"

  echo "Uploading backup bundle to Azure Blob..."
  azcopy copy "$BUNDLE" "$BASE_URL/$PREFIX/$(basename "$BUNDLE")?$SAS_QUERY" --overwrite=true
  azcopy copy "$BUNDLE.sha256" "$BASE_URL/$PREFIX/$(basename "$BUNDLE.sha256")?$SAS_QUERY" --overwrite=true

  AZURE_UPLOAD_STATUS="uploaded"
fi

chmod -R go-rwx "$RUN_DIR" "$BUNDLE" "$BUNDLE.sha256"

cat <<RESULT
status=completed
run_id=$RUN_ID
backup_directory=$RUN_DIR
backup_bundle=$BUNDLE
backup_bundle_sha256=$BUNDLE.sha256
database_dump=$DB_DUMP
config_archive=$CONFIG_ARCHIVE
app_archive=$APP_ARCHIVE
sftp_upload_status=$SFTP_UPLOAD_STATUS
azure_upload_status=$AZURE_UPLOAD_STATUS
RESULT
