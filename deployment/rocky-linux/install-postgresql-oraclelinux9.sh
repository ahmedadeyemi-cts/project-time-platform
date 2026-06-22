#!/usr/bin/env bash
set -euo pipefail

# Project Time Platform
# PostgreSQL native installation script for OCI Oracle Linux 9 development VM.
# End-state may move to Rocky Linux and/or containers, but native PostgreSQL is preferred for this low-memory dev VM.

APP_ROOT="/opt/project-time-platform"
CONFIG_DIR="$APP_ROOT/config"
ENV_FILE="$CONFIG_DIR/postgres.env"
DB_NAME="project_time_platform"
DB_USER="ptp_app"

echo "==> Installing PostgreSQL packages"
sudo dnf install -y postgresql postgresql-server postgresql-contrib

echo "==> Installed PostgreSQL packages"
rpm -qa | grep -i postgresql | sort

echo "==> Initializing PostgreSQL database if needed"
if [ ! -d /var/lib/pgsql/data/base ]; then
  sudo postgresql-setup --initdb
else
  echo "PostgreSQL data directory already appears initialized. Skipping initdb."
fi

echo "==> Enabling and starting PostgreSQL"
sudo systemctl enable --now postgresql
sudo systemctl status postgresql --no-pager

echo "==> Validating PostgreSQL server version"
sudo -u postgres psql -c "SELECT version();"

echo "==> Creating protected config directory"
sudo mkdir -p "$CONFIG_DIR"
sudo chown -R opc:opc "$CONFIG_DIR"
chmod 700 "$CONFIG_DIR"

if [ -f "$ENV_FILE" ]; then
  echo "==> Existing $ENV_FILE found. Reusing existing credentials."
  # shellcheck disable=SC1090
  source "$ENV_FILE"
else
  echo "==> Generating application database credentials"
  APP_DB_PASS=$(openssl rand -base64 32)
  cat > "$ENV_FILE" <<EOF
PTP_DB_NAME=$DB_NAME
PTP_DB_USER=$DB_USER
PTP_DB_PASSWORD=$APP_DB_PASS
PTP_DB_HOST=localhost
PTP_DB_PORT=5432
EOF
  chmod 600 "$ENV_FILE"
  # shellcheck disable=SC1090
  source "$ENV_FILE"
fi

echo "==> Creating PostgreSQL role and database if needed"
sudo -u postgres psql <<SQL
DO \$\$
BEGIN
   IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = '${PTP_DB_USER}') THEN
      CREATE ROLE ${PTP_DB_USER} WITH LOGIN PASSWORD '${PTP_DB_PASSWORD}';
   END IF;
END
\$\$;
SQL

if ! sudo -u postgres psql -tAc "SELECT 1 FROM pg_database WHERE datname='${PTP_DB_NAME}'" | grep -q 1; then
  sudo -u postgres createdb -O "$PTP_DB_USER" "$PTP_DB_NAME"
else
  echo "Database $PTP_DB_NAME already exists."
fi

sudo -u postgres psql -c "GRANT ALL PRIVILEGES ON DATABASE ${PTP_DB_NAME} TO ${PTP_DB_USER};"

echo "==> Validating database role and database"
sudo -u postgres psql -c "\du"
sudo -u postgres psql -c "\l"

echo "==> Testing application database connection"
PGPASSWORD="$PTP_DB_PASSWORD" psql -h "$PTP_DB_HOST" -U "$PTP_DB_USER" -d "$PTP_DB_NAME" -c "SELECT current_database(), current_user, now();"

echo "==> PostgreSQL setup complete"
echo "Credentials saved at $ENV_FILE. Do not commit this file to GitHub."
