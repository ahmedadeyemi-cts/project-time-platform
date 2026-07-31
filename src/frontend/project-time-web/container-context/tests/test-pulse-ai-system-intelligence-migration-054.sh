#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-pulse-ai-054-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/054_pulse_ai_system_intelligence_conversations.sql"
ROLLBACK="/workspace/database/rollback/054_pulse_ai_system_intelligence_conversations_rollback.sql"

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
expect_sql_failure() {
  local sql="$1" expected="$2" label="$3"
  local log="/tmp/pulse-ai-054-${label}.log"
  if psql_exec -c "$sql" >"$log" 2>&1; then
    echo "ASSERTION_FAILED $label unexpectedly_succeeded" >&2
    exit 1
  fi
  grep -Fq "$expected" "$log" || {
    echo "ASSERTION_FAILED $label missing_expected_error=$expected" >&2
    cat "$log" >&2
    exit 1
  }
  echo "ASSERTION_PASSED $label"
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
CREATE TABLE app_users (
  user_id UUID PRIMARY KEY,
  email TEXT NOT NULL UNIQUE,
  display_name TEXT NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE TABLE app_roles (
  app_role_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  role_code TEXT NOT NULL UNIQUE,
  role_name TEXT NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE TABLE app_permissions (
  app_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  permission_code TEXT NOT NULL UNIQUE,
  permission_name TEXT NOT NULL,
  module_code TEXT NOT NULL,
  permission_description TEXT NOT NULL DEFAULT ''
);
CREATE TABLE app_role_permissions (
  app_role_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE CASCADE,
  app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE CASCADE,
  UNIQUE(app_role_id, app_permission_id)
);
CREATE TABLE app_feature_catalog (
  app_feature_catalog_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  feature_code TEXT NOT NULL UNIQUE,
  feature_name TEXT NOT NULL,
  module_code TEXT NOT NULL,
  route_anchor TEXT,
  required_permission_code TEXT,
  feature_description TEXT,
  display_order INTEGER NOT NULL DEFAULT 0,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO app_users(user_id,email,display_name) VALUES
 ('10000000-0000-0000-0000-000000000001','admin@example.test','Administrator'),
 ('10000000-0000-0000-0000-000000000002','lead@example.test','Engineering Lead'),
 ('10000000-0000-0000-0000-000000000003','engineer@example.test','Engineer');
INSERT INTO app_roles(role_code,role_name) VALUES
 ('SUPER_ADMINISTRATOR','Super Administrator'),
 ('ADMINISTRATOR','Administrator'),
 ('PROJECT_TEAM_COORDINATOR','Project Team Coordinator'),
 ('ENGINEERING_LEAD','Engineering Lead'),
 ('ENGINEERING','Engineering'),
 ('PROJECT_MANAGEMENT','Project Management'),
 ('SECURITY_ADMINISTRATOR','Security Administrator'),
 ('RELEASE_MANAGER','Release Manager');
SQL

psql_exec -f "$MIGRATION" >/dev/null
psql_exec -f "$MIGRATION" >/dev/null

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='054_pulse_ai_system_intelligence_conversations';")" migration_registered_once
assert_eq 4 "$(value "SELECT COUNT(*) FROM pg_tables WHERE schemaname='public' AND tablename IN ('pulse_ai_conversations','pulse_ai_conversation_messages','pulse_ai_system_inquiry_runs','pulse_ai_system_tool_events');")" tables_created
assert_eq 7 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code IN ('ASK_PULSE_AI_SYSTEM_INTELLIGENCE','VIEW_PULSE_AI_API_INVENTORY','USE_PULSE_AI_SYSTEM_TROUBLESHOOTING','USE_PULSE_AI_ENHANCEMENT_ADVISOR','VIEW_PULSE_AI_CONVERSATION_HISTORY','RETEST_PULSE_AI_SAFE_API','VIEW_PULSE_AI_SYSTEM_AUDIT');")" permissions_created
assert_eq 5 "$(value "SELECT COUNT(*) FROM app_feature_catalog WHERE feature_code IN ('PULSE_AI_SYSTEM_INTELLIGENCE','PULSE_AI_API_DISCOVERY','PULSE_AI_SYSTEM_TROUBLESHOOTING','PULSE_AI_ENHANCEMENT_ADVISOR','PULSE_AI_CONVERSATIONS');")" features_created
assert_eq 7 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='SUPER_ADMINISTRATOR' AND p.module_code='011';")" super_admin_permissions
# Engineering Lead receives the six read-only intelligence capabilities plus the
# separately granted safe GET retest capability, for seven total permissions.
assert_eq 7 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='ENGINEERING_LEAD' AND p.module_code='011';")" engineering_lead_permissions
assert_eq 3 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='ENGINEERING' AND p.module_code='011';")" engineer_permissions

psql_exec <<'SQL'
INSERT INTO pulse_ai_conversations (
  pulse_ai_conversation_id,actual_user_id,effective_user_id,conversation_mode,title
) VALUES (
  '20000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  'troubleshooting','New Pulse AI conversation'
);
INSERT INTO pulse_ai_conversation_messages (
  pulse_ai_conversation_message_id,pulse_ai_conversation_id,sequence_number,role,
  message_status,message_text,correlation_id
) VALUES (
  '30000000-0000-0000-0000-000000000001',
  '20000000-0000-0000-0000-000000000001',1,'user','completed',
  'Which APIs are running and why is Module 013 failing?','corr-054-test'
);
INSERT INTO pulse_ai_system_inquiry_runs (
  pulse_ai_system_inquiry_run_id,pulse_ai_conversation_id,user_message_id,
  actual_user_id,effective_user_id,intent_code,question_sha256,correlation_id
) VALUES (
  '40000000-0000-0000-0000-000000000001',
  '20000000-0000-0000-0000-000000000001',
  '30000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  'troubleshooting',repeat('a',64),'corr-054-test'
);
INSERT INTO pulse_ai_system_tool_events (
  pulse_ai_system_tool_event_id,pulse_ai_system_inquiry_run_id,tool_code,module_code,
  method,path,event_status,status_code,duration_ms,response_bytes,evidence_json
) VALUES (
  '50000000-0000-0000-0000-000000000001',
  '40000000-0000-0000-0000-000000000001',
  'platform_api_inventory','013','GET','/api/platform-operations/apis',
  'succeeded',200,12.5,512,'{"total":120}'::jsonb
);
SQL

assert_eq 1 "$(value "SELECT message_count FROM pulse_ai_conversations WHERE pulse_ai_conversation_id='20000000-0000-0000-0000-000000000001';")" conversation_message_count
assert_eq 'Which APIs are running and why is Module 013 failing?' "$(value "SELECT title FROM pulse_ai_conversations WHERE pulse_ai_conversation_id='20000000-0000-0000-0000-000000000001';")" conversation_title_from_first_question
assert_eq 1 "$(value "SELECT COUNT(*) FROM pulse_ai_system_tool_events WHERE event_status='succeeded';")" tool_event_persisted
expect_sql_failure "UPDATE pulse_ai_system_tool_events SET event_status='failed';" 'Pulse AI system tool evidence is immutable.' immutable_tool_event_update
expect_sql_failure "DELETE FROM pulse_ai_system_tool_events;" 'Pulse AI system tool evidence is immutable.' immutable_tool_event_delete

psql_exec -f "$ROLLBACK" >/dev/null
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.pulse_ai_conversations')::text,'');")" rollback_removed_conversations
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.pulse_ai_system_tool_events')::text,'');")" rollback_removed_tool_events
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='054_pulse_ai_system_intelligence_conversations';")" rollback_removed_migration
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code='ASK_PULSE_AI_SYSTEM_INTELLIGENCE';")" rollback_removed_permissions

psql_exec -f "$MIGRATION" >/dev/null
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='054_pulse_ai_system_intelligence_conversations';")" safe_reapply

echo 'PULSE_AI_SYSTEM_INTELLIGENCE_MIGRATION_054=PASS'
