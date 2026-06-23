#!/usr/bin/env bash
set -euo pipefail

APP_ROOT="/opt/project-time-platform"
REPO_DIR="$APP_ROOT/app/project-time-platform"
ENV_FILE="$APP_ROOT/config/postgres.env"
MIGRATION_FILE="$REPO_DIR/database/migrations/003_task_based_project_assignments.sql"
MIGRATION_ID="003_task_based_project_assignments"

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

echo "==> Checking project assignment task requirement"
PGPASSWORD="$PTP_DB_PASSWORD" psql \
  -h "$PTP_DB_HOST" \
  -p "$PTP_DB_PORT" \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -c "SELECT column_name, is_nullable FROM information_schema.columns WHERE table_name = 'project_assignments' AND column_name IN ('project_id', 'task_id', 'user_id') ORDER BY column_name;"

echo "==> Checking time entry constraints"
PGPASSWORD="$PTP_DB_PASSWORD" psql \
  -h "$PTP_DB_HOST" \
  -p "$PTP_DB_PORT" \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -c "SELECT constraint_name FROM information_schema.table_constraints WHERE table_name = 'time_entries' AND constraint_name LIKE 'chk_time_entry%' ORDER BY constraint_name;"

echo "==> Migration 003 validation complete"
