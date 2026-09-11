#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-module066-migration-107-${GITHUB_RUN_ID:-local}-$$"
DB_USER='projectpulse'
DB_NAME='projectpulse'
DB_PASSWORD='projectpulse-test-only'
cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT
psql_exec() {
  docker exec -i -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" psql -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" "$@"
}
value() { psql_exec -Atqc "$1" | tr -d '\r'; }
assert_eq() {
  local expected="$1" actual="$2" label="$3"
  [[ "$actual" == "$expected" ]] || { echo "ASSERTION_FAILED $label expected=$expected actual=$actual" >&2; exit 1; }
  echo "ASSERTION_PASSED $label=$actual"
}

docker run -d --name "$CONTAINER" \
  -e POSTGRES_USER="$DB_USER" -e POSTGRES_PASSWORD="$DB_PASSWORD" -e POSTGRES_DB="$DB_NAME" \
  postgres:16-alpine >/dev/null
for attempt in $(seq 1 90); do
  if psql_exec -Atqc 'SELECT 1;' >/dev/null 2>&1; then break; fi
  [[ "$attempt" != 90 ]] || { docker logs "$CONTAINER" >&2 || true; exit 1; }
  sleep 1
done

psql_exec <<'SQL'
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE TABLE schema_migrations(migration_id TEXT PRIMARY KEY, description TEXT, applied_at TIMESTAMPTZ DEFAULT NOW());
CREATE TABLE projects(project_id UUID PRIMARY KEY);
CREATE TABLE app_users(user_id UUID PRIMARY KEY, email TEXT NOT NULL UNIQUE, display_name TEXT NOT NULL, is_active BOOLEAN NOT NULL DEFAULT TRUE);
CREATE TABLE app_permissions(app_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(), permission_code TEXT UNIQUE NOT NULL, permission_name TEXT NOT NULL, module_code TEXT NOT NULL, permission_description TEXT NOT NULL);
CREATE TABLE app_roles(app_role_id UUID PRIMARY KEY DEFAULT gen_random_uuid(), role_code TEXT UNIQUE NOT NULL, is_active BOOLEAN NOT NULL DEFAULT TRUE);
CREATE TABLE app_role_permissions(app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id), app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id), created_at TIMESTAMPTZ DEFAULT NOW(), PRIMARY KEY(app_role_id,app_permission_id));
CREATE TABLE project_flowhive_plans(plan_id UUID PRIMARY KEY);
CREATE TABLE project_notification_dispatches(project_notification_dispatch_id UUID PRIMARY KEY);
CREATE TABLE project_flowhive_customer_shares(share_id UUID PRIMARY KEY);
CREATE TABLE project_flowhive_raid_items(raid_item_id UUID PRIMARY KEY, project_id UUID NOT NULL REFERENCES projects, updated_by_user_id UUID NOT NULL REFERENCES app_users, title TEXT, status TEXT);
INSERT INTO schema_migrations(migration_id) VALUES('086_module_066_flowhive_enterprise_pm');
INSERT INTO projects VALUES('10700000-0000-4000-8000-000000000001');
INSERT INTO app_users(user_id,email,display_name) VALUES
 ('10700000-0000-4000-8000-000000000002','editor-107@example.invalid','Editor 107'),
 ('10700000-0000-4000-8000-000000000003','deleter-107@example.invalid','Deleter 107');
INSERT INTO app_roles(role_code) VALUES('PROJECT_MANAGER');
SQL
psql_exec < "$ROOT/database/migrations/103_module_066_flowhive_enterprise_psa_revamp.sql" >/dev/null
psql_exec < "$ROOT/database/migrations/107_module_066_operation_authorization_and_raid_actor.sql" >/dev/null
psql_exec < "$ROOT/database/migrations/107_module_066_operation_authorization_and_raid_actor.sql" >/dev/null

assert_eq 1 "$(value "SELECT count(*) FROM schema_migrations WHERE migration_id='107_module_066_operation_authorization_and_raid_actor';")" migration_registered_once
assert_eq 1 "$(value "SELECT count(*) FROM app_role_permissions rp JOIN app_roles r USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='PROJECT_MANAGER' AND p.permission_code='MANAGE_FLOWHIVE_MEETINGS_066';")" meeting_permission_granted
assert_eq 1 "$(value "SELECT count(*) FROM app_role_permissions rp JOIN app_roles r USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='PROJECT_MANAGER' AND p.permission_code='MANAGE_FLOWHIVE_TASK_REMINDERS_066';")" reminder_permission_granted

psql_exec <<'SQL'
INSERT INTO project_flowhive_raid_items(raid_item_id,project_id,updated_by_user_id,title,status)
VALUES('10700000-0000-4000-8000-000000000004','10700000-0000-4000-8000-000000000001','10700000-0000-4000-8000-000000000002','Synthetic actor attribution','open');
BEGIN;
SELECT set_config('projectpulse.current_actor','10700000-0000-4000-8000-000000000003',true);
DELETE FROM project_flowhive_raid_items WHERE raid_item_id='10700000-0000-4000-8000-000000000004';
COMMIT;
SQL
assert_eq 1 "$(value "SELECT count(*) FROM project_flowhive_raid_events WHERE action_code='deleted' AND actor_user_id='10700000-0000-4000-8000-000000000003';")" deleting_actor_is_transaction_actor
assert_eq 0 "$(value "SELECT count(*) FROM project_flowhive_raid_events WHERE action_code='deleted' AND actor_user_id='10700000-0000-4000-8000-000000000002';")" stale_editor_is_not_deleting_actor
echo 'MODULE_066_OPERATION_AUTHORIZATION_MIGRATION_107=PASS disposablePostgreSQL=true'
