#!/usr/bin/env bash
set -euo pipefail

APP_ROOT="/opt/project-time-platform"
REPO_DIR="$APP_ROOT/app/project-time-platform"
CSV_FILE="$REPO_DIR/data/holidays/ussignal-paid-holidays-2026.csv"
IMPORTER="$REPO_DIR/deployment/rocky-linux/import-company-holidays.py"
ENV_FILE="$APP_ROOT/config/postgres.env"

if [ ! -f "$CSV_FILE" ]; then
  echo "ERROR: Missing $CSV_FILE"
  exit 1
fi

if [ ! -f "$IMPORTER" ]; then
  echo "ERROR: Missing $IMPORTER"
  exit 1
fi

chmod +x "$IMPORTER"
python3 "$IMPORTER" 2026 "$CSV_FILE" ahmed.adeyemi@ussignal.com

# shellcheck disable=SC1090
source "$ENV_FILE"

echo "==> Imported 2026 holidays"
PGPASSWORD="$PTP_DB_PASSWORD" psql \
  -h "$PTP_DB_HOST" \
  -p "$PTP_DB_PORT" \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -c "SELECT holiday_date, holiday_name, holiday_type, is_floating_holiday, auto_populate_hours FROM company_holidays WHERE EXTRACT(YEAR FROM holiday_date) = 2026 ORDER BY holiday_date;"
