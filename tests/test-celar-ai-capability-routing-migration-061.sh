#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-celar-ai-migration-061-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/061_celar_ai_capability_routing.sql"
ROLLBACK="/workspace/database/rollback/061_celar_ai_capability_routing_rollback.sql"

cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT

psql_exec() {
  docker exec -i -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" \
    psql -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" "$@"
}

value() { psql_exec -Atqc "$1" | tr -d '\r'; }
assert_eq() {
  local expected="$1" actual="$2" label="$3"
  [[ "$actual" == "$expected" ]] || {
    echo "ASSERTION_FAILED $label expected=$expected actual=$actual" >&2
    exit 1
  }
  echo "ASSERTION_PASSED $label=$actual"
}

docker run --detach --rm \
  --name "$CONTAINER" \
  -e POSTGRES_USER="$DB_USER" \
  -e POSTGRES_PASSWORD="$DB_PASSWORD" \
  -e POSTGRES_DB="$DB_NAME" \
  -v "$ROOT:/workspace:ro" \
  postgres:16-alpine >/dev/null

for attempt in $(seq 1 60); do
  if psql_exec -Atqc 'SELECT 1;' >/dev/null 2>&1; then break; fi
  [[ "$attempt" != 60 ]] || { docker logs "$CONTAINER" >&2 || true; exit 1; }
  sleep 1
done

psql_exec <<'SQL'
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE TABLE schema_migrations (
  migration_id TEXT PRIMARY KEY,
  description TEXT NOT NULL DEFAULT '',
  applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
SQL

apply_migration() { psql_exec -f "$MIGRATION" >/dev/null; }

apply_migration
apply_migration
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='061_celar_ai_capability_routing';")" migration_registered_once
assert_eq 7 "$(value "SELECT COUNT(*) FROM ai_capability_routes;")" default_routes_complete
assert_eq 1 "$(value "SELECT COUNT(*) FROM ai_capability_routes WHERE feature_code='help_assistant' AND route_targets='[\"celar_ai\",\"claude\",\"openai\",\"local_template\"]'::jsonb;")" help_route_order
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='061_celar_ai_capability_routing' AND description <> '';")" migration_description_recorded

psql_exec -f "$ROLLBACK" >/dev/null
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='061_celar_ai_capability_routing';")" rollback_removed_registration
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.ai_capability_routes')::text,'');")" rollback_removed_routes
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.ai_private_model_profiles')::text,'');")" rollback_removed_private_profiles

apply_migration
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='061_celar_ai_capability_routing';")" migration_reapplied
assert_eq 7 "$(value "SELECT COUNT(*) FROM ai_capability_routes;")" routes_reapplied

echo 'CELAR_AI_CAPABILITY_ROUTING_MIGRATION_061=PASS'
