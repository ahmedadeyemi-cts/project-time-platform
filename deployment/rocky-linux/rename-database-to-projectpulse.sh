#!/usr/bin/env bash
set -euo pipefail

APP_ROOT="/opt/project-time-platform"
ENV_FILE="$APP_ROOT/config/postgres.env"
OLD_DEFAULT="project_time_platform"
NEW_DB_NAME="Project Health Dashboard"
SERVICE_NAME="projecttime-api.service"

if [ ! -f "$ENV_FILE" ]; then
  echo "ERROR: Missing $ENV_FILE"
  exit 1
fi

# shellcheck disable=SC1090
source "$ENV_FILE"

OLD_DB_NAME="${PTP_DB_NAME:-$OLD_DEFAULT}"

quote_sql_literal() {
  printf "%s" "$1" | sed "s/'/''/g"
}

quote_sql_identifier() {
  printf "%s" "$1" | sed 's/"/""/g'
}

OLD_LITERAL=$(quote_sql_literal "$OLD_DB_NAME")
NEW_LITERAL=$(quote_sql_literal "$NEW_DB_NAME")
OLD_IDENTIFIER=$(quote_sql_identifier "$OLD_DB_NAME")
NEW_IDENTIFIER=$(quote_sql_identifier "$NEW_DB_NAME")

old_exists=$(sudo -u postgres psql -d postgres -At -c "SELECT CASE WHEN EXISTS (SELECT 1 FROM pg_database WHERE datname = '$OLD_LITERAL') THEN 'yes' ELSE 'no' END;")
new_exists=$(sudo -u postgres psql -d postgres -At -c "SELECT CASE WHEN EXISTS (SELECT 1 FROM pg_database WHERE datname = '$NEW_LITERAL') THEN 'yes' ELSE 'no' END;")

if [ "$OLD_DB_NAME" = "$NEW_DB_NAME" ] && [ "$new_exists" = "yes" ]; then
  echo "==> Database is already named $NEW_DB_NAME."
else
  if [ "$old_exists" != "yes" ] && [ "$new_exists" = "yes" ]; then
    echo "==> Source database $OLD_DB_NAME was not found, but $NEW_DB_NAME already exists. Updating env validation only."
  elif [ "$old_exists" = "yes" ] && [ "$new_exists" = "yes" ]; then
    echo "ERROR: Both $OLD_DB_NAME and $NEW_DB_NAME exist. Resolve manually before continuing."
    exit 1
  elif [ "$old_exists" != "yes" ]; then
    echo "ERROR: Source database $OLD_DB_NAME does not exist."
    exit 1
  else
    echo "==> Stopping API service to release database connections"
    sudo systemctl stop "$SERVICE_NAME" || true

    echo "==> Terminating active sessions for $OLD_DB_NAME"
    sudo -u postgres psql -d postgres -v ON_ERROR_STOP=1 -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$OLD_LITERAL' AND pid <> pg_backend_pid();"

    echo "==> Renaming database $OLD_DB_NAME to $NEW_DB_NAME"
    sudo -u postgres psql -d postgres -v ON_ERROR_STOP=1 -c "ALTER DATABASE \"$OLD_IDENTIFIER\" RENAME TO \"$NEW_IDENTIFIER\";"
  fi
fi

backup_file="$ENV_FILE.before-projectpulse-rename.$(date +%Y%m%d%H%M%S)"
cp "$ENV_FILE" "$backup_file"
chmod 600 "$backup_file"

echo "==> Updating $ENV_FILE with PTP_DB_NAME=$NEW_DB_NAME"
if grep -q '^PTP_DB_NAME=' "$ENV_FILE"; then
  sed -i "s/^PTP_DB_NAME=.*/PTP_DB_NAME=$NEW_DB_NAME/" "$ENV_FILE"
else
  echo "PTP_DB_NAME=$NEW_DB_NAME" >> "$ENV_FILE"
fi
chmod 600 "$ENV_FILE"

# shellcheck disable=SC1090
source "$ENV_FILE"

echo "==> Validating application login to renamed database"
PGPASSWORD="$PTP_DB_PASSWORD" psql \
  -h "$PTP_DB_HOST" \
  -p "$PTP_DB_PORT" \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -c "SELECT current_database(), current_user, NOW();"

echo "==> Starting API service"
sudo systemctl start "$SERVICE_NAME" || true

echo "==> Project Health Dashboard database rename complete"
echo "Backup env file: $backup_file"
