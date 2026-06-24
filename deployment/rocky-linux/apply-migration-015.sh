#!/usr/bin/env bash
set -euo pipefail

APP_ROOT="/opt/project-time-platform"
REPO_DIR="$APP_ROOT/app/project-time-platform"
ENV_FILE="$APP_ROOT/config/postgres.env"
MIGRATION_FILE="$REPO_DIR/database/migrations/015_department_manager_and_user_switcher.sql"
MIGRATION_ID="015_department_manager_and_user_switcher"

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

echo "==> Departments"
PGPASSWORD="$PTP_DB_PASSWORD" psql \
  -h "$PTP_DB_HOST" \
  -p "$PTP_DB_PORT" \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -c "SELECT d.department_name, manager.email AS manager_email, d.is_active FROM app_departments d LEFT JOIN app_users manager ON manager.user_id = d.manager_user_id ORDER BY d.display_order;"

echo "==> Active users and roles"
PGPASSWORD="$PTP_DB_PASSWORD" psql \
  -h "$PTP_DB_HOST" \
  -p "$PTP_DB_PORT" \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -c "SELECT u.email, u.department, COALESCE(string_agg(r.role_name, ', ' ORDER BY r.display_order), 'No role') AS roles FROM app_users u LEFT JOIN app_user_role_assignments ura ON ura.user_id = u.user_id AND ura.is_active = TRUE LEFT JOIN app_roles r ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE WHERE u.is_active = TRUE GROUP BY u.email, u.department ORDER BY u.email;"

echo "==> Migration 015 validation complete"
