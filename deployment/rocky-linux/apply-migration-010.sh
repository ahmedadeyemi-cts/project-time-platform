#!/usr/bin/env bash
set -euo pipefail

APP_ROOT="/opt/project-time-platform"
REPO_DIR="$APP_ROOT/app/project-time-platform"
ENV_FILE="$APP_ROOT/config/postgres.env"
MIGRATION_FILE="$REPO_DIR/database/migrations/010_psa_demo_assignment_visibility.sql"
MIGRATION_ID="010_psa_demo_assignment_visibility"

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

echo "==> Validating open task assignment visibility for week 2026-06-21"
PGPASSWORD="$PTP_DB_PASSWORD" psql \
  -h "$PTP_DB_HOST" \
  -p "$PTP_DB_PORT" \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -c "SELECT p.project_code, pt.task_code, pt.task_name, pa.effective_start_date, pa.effective_end_date, u.email AS assigned_user FROM project_assignments pa JOIN projects p ON p.project_id = pa.project_id JOIN project_tasks pt ON pt.task_id = pa.task_id JOIN app_users u ON u.user_id = pa.user_id WHERE p.project_code = 'USS-PSA-2026' AND u.email = 'ahmed.adeyemi@ussignal.com' ORDER BY pt.task_code;"

echo "==> Migration 010 validation complete"
