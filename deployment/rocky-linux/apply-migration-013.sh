#!/usr/bin/env bash
set -euo pipefail

APP_ROOT="/opt/project-time-platform"
REPO_DIR="$APP_ROOT/app/project-time-platform"
ENV_FILE="$APP_ROOT/config/postgres.env"
MIGRATION_FILE="$REPO_DIR/database/migrations/013_role_based_access_control.sql"
MIGRATION_ID="013_role_based_access_control"

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

echo "==> Validating RBAC tables"
PGPASSWORD="$PTP_DB_PASSWORD" psql \
  -h "$PTP_DB_HOST" \
  -p "$PTP_DB_PORT" \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -c "SELECT 'app_roles' AS table_name, COUNT(*) FROM app_roles UNION ALL SELECT 'app_permissions', COUNT(*) FROM app_permissions UNION ALL SELECT 'app_role_permissions', COUNT(*) FROM app_role_permissions UNION ALL SELECT 'app_user_role_assignments', COUNT(*) FROM app_user_role_assignments UNION ALL SELECT 'app_feature_catalog', COUNT(*) FROM app_feature_catalog ORDER BY table_name;"

echo "==> Role assignments"
PGPASSWORD="$PTP_DB_PASSWORD" psql \
  -h "$PTP_DB_HOST" \
  -p "$PTP_DB_PORT" \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -c "SELECT u.email, r.role_code, r.role_name FROM app_user_role_assignments ura JOIN app_users u ON u.user_id = ura.user_id JOIN app_roles r ON r.app_role_id = ura.app_role_id WHERE ura.is_active = TRUE ORDER BY u.email, r.display_order;"

echo "==> Migration 013 validation complete"
