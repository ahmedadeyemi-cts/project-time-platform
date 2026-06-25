#!/usr/bin/env bash
set -euo pipefail

APP_ROOT="/opt/project-time-platform"
REPO_DIR="$APP_ROOT/app/project-time-platform"
ENV_FILE="$APP_ROOT/config/postgres.env"
MIGRATION_FILE="$REPO_DIR/database/migrations/006_timesheet_persistence_location_columns.sql"
MIGRATION_ID="006_timesheet_persistence_location_columns"

if [ ! -f "$ENV_FILE" ]; then
  echo "ERROR: Missing $ENV_FILE"
  exit 1
fi

if [ ! -f "$MIGRATION_FILE" ]; then
  echo "ERROR: Missing $MIGRATION_FILE"
  exit 1
fi

# shellcheck disable=SC1090
source "$ENV_FILE"

is_applied=$(PGPASSWORD="$PTP_DB_PASSWORD" psql \
  -h "$PTP_DB_HOST" \
  -p "$PTP_DB_PORT" \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -At \
  -c "SELECT CASE WHEN EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id = '$MIGRATION_ID') THEN 'yes' ELSE 'no' END;")

if [ "$is_applied" = "yes" ]; then
  echo "==> Migration $MIGRATION_ID is already applied. Skipping schema apply step."
else
  echo "==> Applying migration: $MIGRATION_FILE"
  PGPASSWORD="$PTP_DB_PASSWORD" psql \
    -h "$PTP_DB_HOST" \
    -p "$PTP_DB_PORT" \
    -U "$PTP_DB_USER" \
    -d "$PTP_DB_NAME" \
    -v ON_ERROR_STOP=1 \
    -f "$MIGRATION_FILE"
fi

echo "==> Validating migration"
PGPASSWORD="$PTP_DB_PASSWORD" psql \
  -h "$PTP_DB_HOST" \
  -p "$PTP_DB_PORT" \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -c "SELECT migration_id, description, applied_at FROM schema_migrations ORDER BY applied_at;"

echo "==> Checking time entry location columns"
PGPASSWORD="$PTP_DB_PASSWORD" psql \
  -h "$PTP_DB_HOST" \
  -p "$PTP_DB_PORT" \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -c "SELECT column_name, data_type, is_nullable FROM information_schema.columns WHERE table_name = 'time_entries' AND column_name IN ('work_location_group_id', 'work_location_id') ORDER BY column_name;"

echo "==> Migration 006 validation complete"
