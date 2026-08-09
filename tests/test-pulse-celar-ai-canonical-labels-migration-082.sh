#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-canonical-labels-082-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/082_pulse_celar_ai_canonical_labels.sql"

cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT
psql_db_exec() { local database="$1"; shift; docker exec -i -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" psql -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$database" "$@"; }
psql_exec() { psql_db_exec "$DB_NAME" "$@"; }
value() { psql_exec -Atqc "$1" | tr -d '\r'; }
database_value() { local database="$1" query="$2"; psql_db_exec "$database" -Atqc "$query" | tr -d '\r'; }
assert_eq() { local expected="$1" actual="$2" label="$3"; [[ "$actual" == "$expected" ]] || { echo "ASSERTION_FAILED $label expected=$expected actual=$actual" >&2; exit 1; }; echo "ASSERTION_PASSED $label=$actual"; }

docker run --detach --rm --name "$CONTAINER" \
  -e POSTGRES_USER="$DB_USER" -e POSTGRES_PASSWORD="$DB_PASSWORD" -e POSTGRES_DB="$DB_NAME" \
  -v "$ROOT:/workspace:ro" postgres:16-alpine >/dev/null

for attempt in $(seq 1 60); do
  psql_exec -Atqc 'SELECT 1' >/dev/null 2>&1 && break
  [[ "$attempt" != 60 ]] || { docker logs "$CONTAINER" >&2 || true; exit 1; }
  sleep 1
done

psql_exec <<'SQL'
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE TABLE schema_migrations(migration_id TEXT PRIMARY KEY,description TEXT NOT NULL DEFAULT '',applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
INSERT INTO schema_migrations(migration_id,description) VALUES
 ('075_pulse_product_rebrand','Preserved historical ProjectPulse migration evidence'),
 ('054_pulse_ai_system_intelligence_conversations','Preserved historical Pulse AI migration evidence');

CREATE TABLE app_roles(role_code TEXT PRIMARY KEY,role_name TEXT NOT NULL,role_description TEXT,updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
CREATE TABLE app_permissions(permission_code TEXT PRIMARY KEY,permission_name TEXT NOT NULL,permission_description TEXT,updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
CREATE TABLE app_feature_catalog(feature_code TEXT PRIMARY KEY,feature_name TEXT NOT NULL,feature_description TEXT,updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
CREATE TABLE auth_identity_providers(provider_code TEXT PRIMARY KEY,provider_name TEXT NOT NULL,updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
CREATE TABLE reminder_rules(rule_code TEXT PRIMARY KEY,rule_name TEXT NOT NULL,subject_template TEXT NOT NULL,body_template TEXT NOT NULL,updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
CREATE TABLE enterprise_notification_policies(policy_code TEXT PRIMARY KEY,policy_name TEXT NOT NULL,subject_template TEXT NOT NULL,text_template TEXT NOT NULL,updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
CREATE TABLE system_email_provider_consumers(consumer_key TEXT PRIMARY KEY,consumer_name TEXT NOT NULL,consumer_description TEXT NOT NULL,updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
CREATE TABLE signed_sow_handoff_notification_templates(template_key TEXT PRIMARY KEY,subject_template TEXT NOT NULL,body_template TEXT NOT NULL);
CREATE TABLE reporting_external_connection_catalog(connection_key TEXT PRIMARY KEY,connection_name TEXT NOT NULL,updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
CREATE TABLE crm_integration_field_mappings(mapping_id BIGSERIAL PRIMARY KEY,projectpulse_destination TEXT NOT NULL,updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
CREATE TABLE projects(project_code TEXT PRIMARY KEY,project_description TEXT);
CREATE TABLE expense_reports(report_number TEXT PRIMARY KEY,report_title TEXT);

INSERT INTO app_roles VALUES ('SUPER_ADMINISTRATOR','ProjectPulse Administrator','Full control across every Project Pulse module.',NOW());
INSERT INTO app_permissions VALUES ('ASK_PULSE_AI_HELP_SEARCH','Ask Pulse AI Help and Search','Use ProjectPulse product help.',NOW());
INSERT INTO app_feature_catalog VALUES ('PULSE_AI_PRIVATE_HELP_SEARCH','Pulse AI Private Help and Search','ProjectPulse help with citations.',NOW());
INSERT INTO auth_identity_providers VALUES ('LOCAL','Project Pulse Local Authentication',NOW());
INSERT INTO reminder_rules VALUES ('WEEKLY','ProjectPulse weekly reminder','Reminder: Submit time in Project Pulse','Open ProjectPulse to submit time.',NOW());
INSERT INTO enterprise_notification_policies VALUES ('TIME_READY','ProjectPulse time ready','ProjectPulse: Time ready','Open ProjectPulse.',NOW());
INSERT INTO system_email_provider_consumers VALUES ('TIME','ProjectPulse Mail','Shared ProjectPulse email provider.',NOW());
INSERT INTO signed_sow_handoff_notification_templates VALUES ('ASSIGNED','ProjectPulse assignment','Open ProjectPulse Project Workspace.');
INSERT INTO reporting_external_connection_catalog VALUES ('postgresql','PostgreSQL ProjectPulse Database',NOW());
INSERT INTO crm_integration_field_mappings(projectpulse_destination) VALUES ('ProjectPulse Customer');
INSERT INTO projects VALUES ('USS-PSA-2026','Internal Project Pulse foundation project.'),('CUSTOMER-1','Customer chose the Project Pulse delivery name.');
INSERT INTO expense_reports VALUES ('EXP-2026-0001','Project Pulse validation expenses'),('CUSTOMER-EXPENSE','Project Pulse customer-authored title');

CREATE TABLE pulse_ai_conversations(
  pulse_ai_conversation_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  title TEXT NOT NULL DEFAULT 'New Pulse AI conversation',
  message_count INTEGER NOT NULL DEFAULT 0,
  last_message_at TIMESTAMPTZ,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE pulse_ai_conversation_messages(
  pulse_ai_conversation_message_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  pulse_ai_conversation_id UUID NOT NULL REFERENCES pulse_ai_conversations,
  role TEXT NOT NULL,
  message_text TEXT NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE FUNCTION pulse_ai_054_increment_conversation() RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
 UPDATE pulse_ai_conversations SET message_count=message_count+1,last_message_at=NEW.created_at,updated_at=NOW(),title=CASE WHEN NEW.role='user' AND title='New Pulse AI conversation' THEN NEW.message_text ELSE title END WHERE pulse_ai_conversation_id=NEW.pulse_ai_conversation_id;
 RETURN NEW;
END $$;
CREATE TRIGGER trg_message AFTER INSERT ON pulse_ai_conversation_messages FOR EACH ROW EXECUTE FUNCTION pulse_ai_054_increment_conversation();

CREATE FUNCTION pulse_ai_052_block_processing_event_mutation() RETURNS TRIGGER LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Pulse AI document processing event evidence is immutable.'; END $$;
CREATE FUNCTION pulse_ai_053_block_retrieval_event_mutation() RETURNS TRIGGER LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Pulse AI retrieval event evidence is immutable.'; END $$;
CREATE FUNCTION pulse_ai_054_block_tool_event_mutation() RETURNS TRIGGER LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Pulse AI system tool evidence is immutable.'; END $$;
CREATE FUNCTION projectpulse048_block_system_audit_mutation() RETURNS TRIGGER LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'ProjectPulse system audit evidence is immutable.'; END $$;
CREATE FUNCTION projectpulse_056_block_immutable_mutation() RETURNS TRIGGER LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'ProjectPulse migration 056 evidence is immutable.'; END $$;
CREATE FUNCTION projectpulse_056a_block_immutable_mutation() RETURNS TRIGGER LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'ProjectPulse migration 056A evidence is immutable.'; END $$;
CREATE FUNCTION projectpulse_062_block_evidence_mutation() RETURNS TRIGGER LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'ProjectPulse migration 062 evidence is immutable.'; END $$;
CREATE FUNCTION projectpulse_063_block_evidence_mutation() RETURNS TRIGGER LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'ProjectPulse migration 063 evidence is immutable.'; END $$;
CREATE FUNCTION projectpulse066_guard_issued_project_number() RETURNS TRIGGER LANGUAGE plpgsql AS $$ BEGIN RETURN NEW; END $$;

INSERT INTO pulse_ai_conversations(title) VALUES ('New Pulse AI conversation');
SQL

psql_exec -f "$MIGRATION" >/dev/null
first_applied="$(value "SELECT applied_at FROM schema_migrations WHERE migration_id='082_pulse_celar_ai_canonical_labels'")"
psql_exec -f "$MIGRATION" >/dev/null

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='082_pulse_celar_ai_canonical_labels'")" migration_registered_once
assert_eq "$first_applied" "$(value "SELECT applied_at FROM schema_migrations WHERE migration_id='082_pulse_celar_ai_canonical_labels'")" migration_timestamp_idempotent
assert_eq 'Preserved historical ProjectPulse migration evidence' "$(value "SELECT description FROM schema_migrations WHERE migration_id='075_pulse_product_rebrand'")" historical_migration_evidence_unchanged
assert_eq 'ASK_PULSE_AI_HELP_SEARCH' "$(value "SELECT permission_code FROM app_permissions")" permission_code_preserved
assert_eq 'Ask Celar AI Help and Search' "$(value "SELECT permission_name FROM app_permissions")" permission_label_rebranded
assert_eq 'Celar AI Private Help and Search' "$(value "SELECT feature_name FROM app_feature_catalog")" feature_label_rebranded
assert_eq 'Pulse Local Authentication' "$(value "SELECT provider_name FROM auth_identity_providers")" provider_label_rebranded
assert_eq 'Pulse: Time ready' "$(value "SELECT subject_template FROM enterprise_notification_policies")" notification_subject_rebranded
assert_eq 'PostgreSQL Pulse Database' "$(value "SELECT connection_name FROM reporting_external_connection_catalog")" connection_label_rebranded
assert_eq 'Pulse Customer' "$(value "SELECT projectpulse_destination FROM crm_integration_field_mappings")" mapping_label_rebranded
assert_eq 'Internal Pulse foundation project.' "$(value "SELECT project_description FROM projects WHERE project_code='USS-PSA-2026'")" known_seed_project_rebranded
assert_eq 'Customer chose the Project Pulse delivery name.' "$(value "SELECT project_description FROM projects WHERE project_code='CUSTOMER-1'")" customer_project_content_preserved
assert_eq 'Pulse validation expenses' "$(value "SELECT report_title FROM expense_reports WHERE report_number='EXP-2026-0001'")" known_seed_expense_rebranded
assert_eq 'Project Pulse customer-authored title' "$(value "SELECT report_title FROM expense_reports WHERE report_number='CUSTOMER-EXPENSE'")" customer_expense_content_preserved
assert_eq "'New Celar AI conversation'::text" "$(value "SELECT column_default FROM information_schema.columns WHERE table_schema='public' AND table_name='pulse_ai_conversations' AND column_name='title'")" conversation_default_rebranded
assert_eq 'New Celar AI conversation' "$(value "SELECT title FROM pulse_ai_conversations LIMIT 1")" placeholder_title_rebranded

conversation_id="$(value "SELECT pulse_ai_conversation_id FROM pulse_ai_conversations LIMIT 1")"
psql_exec -qc "INSERT INTO pulse_ai_conversation_messages(pulse_ai_conversation_id,role,message_text) VALUES('$conversation_id','user','How do I use Pulse?')"
assert_eq 'How do I use Pulse?' "$(value "SELECT title FROM pulse_ai_conversations WHERE pulse_ai_conversation_id='$conversation_id'")" canonical_placeholder_promoted_from_first_question

assert_eq 0 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_name LIKE '%Pulse AI%' OR permission_description LIKE '%ProjectPulse%' OR permission_description LIKE '%Project Pulse%'")" permission_visible_legacy_labels_removed
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_feature_catalog WHERE feature_name LIKE '%Pulse AI%' OR feature_description LIKE '%ProjectPulse%' OR feature_description LIKE '%Project Pulse%'")" feature_visible_legacy_labels_removed
assert_eq 0 "$(value "SELECT COUNT(*) FROM enterprise_notification_policies WHERE subject_template LIKE '%ProjectPulse%' OR text_template LIKE '%ProjectPulse%'")" notification_visible_legacy_labels_removed
assert_eq t "$(value "SELECT pg_get_functiondef('projectpulse066_guard_issued_project_number()'::regprocedure) LIKE '%projectpulse.project_number_issuance%'")" project_number_compatibility_setting_preserved
assert_eq f "$(value "SELECT pg_get_functiondef('pulse_ai_052_block_processing_event_mutation()'::regprocedure) LIKE '%Pulse AI document%'")" processing_function_legacy_message_removed
assert_eq t "$(value "SELECT pg_get_functiondef('pulse_ai_052_block_processing_event_mutation()'::regprocedure) LIKE '%Celar AI document%'")" processing_function_canonical_message_present
assert_eq t "$(value "SELECT pg_get_functiondef('projectpulse048_block_system_audit_mutation()'::regprocedure) LIKE '%Pulse system audit%'")" audit_function_canonical_message_present

# Optional application tables and functions must remain genuinely optional. A
# ledger-only database proves the migration can converge safely during staged
# or partial environment recovery.
psql_db_exec postgres -qc 'CREATE DATABASE projectpulse_082_minimal'
psql_db_exec projectpulse_082_minimal <<'SQL'
CREATE TABLE schema_migrations(migration_id TEXT PRIMARY KEY,description TEXT NOT NULL DEFAULT '',applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
INSERT INTO schema_migrations(migration_id,description) VALUES ('075_pulse_product_rebrand','Minimal prerequisite');
SQL
psql_db_exec projectpulse_082_minimal -f "$MIGRATION" >/dev/null
assert_eq 1 "$(database_value projectpulse_082_minimal "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='082_pulse_celar_ai_canonical_labels'")" optional_objects_absent_safe

echo 'PULSE_CELAR_AI_CANONICAL_LABELS_MIGRATION_082=PASS'
