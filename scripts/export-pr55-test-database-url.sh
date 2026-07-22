#!/usr/bin/env bash
set -Eeuo pipefail

RESOURCE_GROUP="${AZURE_RESOURCE_GROUP:-}"
API_APP="${AZURE_API_APP:-}"
GITHUB_ENV_FILE="${GITHUB_ENV:-}"

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "Required command not found: $1"
}

normalize_az_value() {
  case "${1:-}" in
    ""|None|null) return 1 ;;
    *) printf '%s\n' "$1" ;;
  esac
}

mask_value() {
  local value="$1"
  value="${value//%/%25}"
  printf '::add-mask::%s\n' "$value"
}

read_container_env() {
  local name="$1"
  local value secret_ref

  value="$(az containerapp show \
    --resource-group "$RESOURCE_GROUP" \
    --name "$API_APP" \
    --query "properties.template.containers[0].env[?name=='$name'].value | [0]" \
    --output tsv \
    --only-show-errors)"
  if normalize_az_value "$value"; then
    return 0
  fi

  secret_ref="$(az containerapp show \
    --resource-group "$RESOURCE_GROUP" \
    --name "$API_APP" \
    --query "properties.template.containers[0].env[?name=='$name'].secretRef | [0]" \
    --output tsv \
    --only-show-errors)"
  normalize_az_value "$secret_ref" >/dev/null || return 1

  value="$(az containerapp secret list \
    --resource-group "$RESOURCE_GROUP" \
    --name "$API_APP" \
    --show-values \
    --query "[?name=='$secret_ref'].value | [0]" \
    --output tsv \
    --only-show-errors)"
  normalize_az_value "$value"
}

[[ -n "$RESOURCE_GROUP" ]] || fail "AZURE_RESOURCE_GROUP is not configured."
[[ -n "$API_APP" ]] || fail "AZURE_API_APP is not configured."
[[ -n "$GITHUB_ENV_FILE" ]] || fail "GITHUB_ENV is not available."

require_command az
require_command python3

DB_HOST="$(read_container_env PTP_DB_HOST)" ||
  fail "PTP_DB_HOST is not configured on the test API Container App."
DB_PORT="$(read_container_env PTP_DB_PORT || true)"
DB_USER="$(read_container_env PTP_DB_USER)" ||
  fail "PTP_DB_USER is not configured on the test API Container App."
DB_NAME="$(read_container_env PTP_DB_NAME)" ||
  fail "PTP_DB_NAME is not configured on the test API Container App."
DB_PASSWORD="$(read_container_env PTP_DB_PASSWORD)" ||
  fail "PTP_DB_PASSWORD or its referenced Container App secret is unavailable."
DB_SSLMODE="$(read_container_env PTP_DB_SSLMODE || true)"

DB_PORT="${DB_PORT:-5432}"
DB_SSLMODE="${DB_SSLMODE:-require}"

[[ "$DB_HOST" != *[$'\r\n\t /@']* && "$DB_HOST" != *:* ]] ||
  fail "PTP_DB_HOST is not a valid DNS host."
[[ "$DB_PORT" =~ ^[0-9]+$ ]] && (( DB_PORT >= 1 && DB_PORT <= 65535 )) ||
  fail "PTP_DB_PORT is not a valid TCP port."
case "$DB_SSLMODE" in
  require|verify-ca|verify-full) ;;
  *) fail "PTP_DB_SSLMODE must be require, verify-ca, or verify-full." ;;
esac
[[ "$DB_USER" != *$'\n'* && "$DB_NAME" != *$'\n'* && "$DB_PASSWORD" != *$'\n'* ]] ||
  fail "Database configuration contains an unsupported newline."

mask_value "$DB_USER"
mask_value "$DB_PASSWORD"

PROJECTPULSE_TEST_DATABASE_URL="$(
  DB_HOST="$DB_HOST" \
  DB_PORT="$DB_PORT" \
  DB_USER="$DB_USER" \
  DB_NAME="$DB_NAME" \
  DB_PASSWORD="$DB_PASSWORD" \
  DB_SSLMODE="$DB_SSLMODE" \
  python3 - <<'PY'
import os
from urllib.parse import quote

host = os.environ["DB_HOST"]
port = os.environ["DB_PORT"]
user = quote(os.environ["DB_USER"], safe="")
password = quote(os.environ["DB_PASSWORD"], safe="")
database = quote(os.environ["DB_NAME"], safe="")
sslmode = quote(os.environ["DB_SSLMODE"], safe="")

print(f"postgresql://{user}:{password}@{host}:{port}/{database}?sslmode={sslmode}")
PY
)"

[[ -n "$PROJECTPULSE_TEST_DATABASE_URL" ]] ||
  fail "The PostgreSQL connection URI could not be constructed."

mask_value "$PROJECTPULSE_TEST_DATABASE_URL"
printf 'PROJECTPULSE_TEST_DATABASE_URL=%s\n' "$PROJECTPULSE_TEST_DATABASE_URL" >> "$GITHUB_ENV_FILE"

echo "PR55_DATABASE_CONFIG_SOURCE=AZURE_CONTAINER_APP"
echo "PR55_DATABASE_CONFIG_READY=YES"

unset DB_PASSWORD PROJECTPULSE_TEST_DATABASE_URL
