#!/usr/bin/env bash
set -euo pipefail

APP_ROOT="/opt/project-time-platform"
REPO_DIR="$APP_ROOT/app/project-time-platform"
ENV_FILE="$APP_ROOT/config/postgres.env"
MIGRATION_FILE="$REPO_DIR/database/migrations/016_auth_identity_foundation.sql"
MIGRATION_ID="016_auth_identity_foundation"

source "$ENV_FILE"

is_applied=$(PGPASSWORD="$PTP_DB_PASSWORD" psql \
  -h "$PTP_DB_HOST" \
  -p "$PTP_DB_PORT" \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -At \
  -c "SELECT CASE WHEN EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id = '$MIGRATION_ID') THEN 'yes' ELSE 'no' END;")

if [ "$is_applied" = "yes" ]; then
  echo "==> Migration $MIGRATION_ID is already applied. Skipping."
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

echo "==> Local accounts"
PGPASSWORD="$PTP_DB_PASSWORD" psql \
  -h "$PTP_DB_HOST" \
  -p "$PTP_DB_PORT" \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -c "SELECT la.username, u.display_name, la.must_change_password, la.is_active FROM auth_local_accounts la JOIN app_users u ON u.user_id = la.user_id ORDER BY la.username;"

echo "==> External identity links"
PGPASSWORD="$PTP_DB_PASSWORD" psql \
  -h "$PTP_DB_HOST" \
  -p "$PTP_DB_PORT" \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -c "SELECT provider_code, user_principal_name, email, is_active FROM auth_external_identity_links ORDER BY provider_code, email;"

echo "==> Migration 016 validation complete"
