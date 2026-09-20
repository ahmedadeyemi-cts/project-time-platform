#!/usr/bin/env bash
set -euo pipefail
: "${ENTERPRISE_COMPLETION_DATABASE_URL:?Use a disposable local enterprise_completion_test database}"
[[ "$ENTERPRISE_COMPLETION_DATABASE_URL" =~ ^postgres(ql)?://[^/]+@(localhost|127\.0\.0\.1):[0-9]+/enterprise_completion_test$ ]] || { echo 'Disposable local test database required'; exit 1; }
psql "$ENTERPRISE_COMPLETION_DATABASE_URL" -X -v ON_ERROR_STOP=1 <<'SQL'
CREATE TABLE schema_migrations(migration_id text PRIMARY KEY, description text NOT NULL);
CREATE TABLE ai_provider_secrets(provider_code text PRIMARY KEY CONSTRAINT ck_ai_provider_secrets_provider_code CHECK(provider_code IN ('deepseek_v4','claude','openai')));
CREATE TABLE ai_provider_settings(provider_code text PRIMARY KEY CONSTRAINT ck_ai_provider_settings_provider_code CHECK(provider_code IN ('deepseek_v4','claude','openai')));
CREATE TABLE ai_private_model_profiles(environment_code text PRIMARY KEY, revision int NOT NULL DEFAULT 1);
INSERT INTO ai_private_model_profiles VALUES('test',4);
SQL
for attempt in 1 2; do
  for migration in database/migrations/{112_optional_ai_providers,113_module065_teams_notifications,114_module064_private_admin_override}.sql; do
    psql "$ENTERPRISE_COMPLETION_DATABASE_URL" -X -v ON_ERROR_STOP=1 -f "$migration" >/dev/null
  done
done
psql "$ENTERPRISE_COMPLETION_DATABASE_URL" -X -v ON_ERROR_STOP=1 <<'SQL'
INSERT INTO ai_provider_secrets VALUES('gemini'),('copilot_studio');
INSERT INTO ai_provider_settings VALUES('gemini'),('copilot_studio');
INSERT INTO module065_teams_configuration(environment,updated_by) VALUES('test','00000000-0000-0000-0000-000000000001');
DO $$ BEGIN
 IF (SELECT count(*) FROM schema_migrations) <> 3 THEN RAISE EXCEPTION 'Migration idempotency failed'; END IF;
 IF (SELECT administrator_override FROM ai_private_model_profiles WHERE environment_code='test') THEN RAISE EXCEPTION 'Migration activated old private settings'; END IF;
 IF (SELECT enabled FROM module065_teams_configuration WHERE environment='test') THEN RAISE EXCEPTION 'Teams enabled implicitly'; END IF;
 BEGIN INSERT INTO ai_provider_settings VALUES('unknown'); RAISE EXCEPTION 'Unknown provider accepted'; EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN INSERT INTO module065_teams_configuration(environment,updated_by) VALUES('unknown','00000000-0000-0000-0000-000000000001'); RAISE EXCEPTION 'Unknown environment accepted'; EXCEPTION WHEN check_violation THEN NULL; END;
END $$;
INSERT INTO module065_teams_delivery(delivery_id,dispatch_id,environment,recipient,status) VALUES('00000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000002','test','test@example.invalid','sending');
INSERT INTO module065_teams_delivery(delivery_id,dispatch_id,environment,recipient,status) VALUES('00000000-0000-0000-0000-000000000003','00000000-0000-0000-0000-000000000002','test','test@example.invalid','sending') ON CONFLICT(dispatch_id,environment,recipient) DO NOTHING;
DO $$ BEGIN IF (SELECT count(*) FROM module065_teams_delivery) <> 1 THEN RAISE EXCEPTION 'Duplicate delivery claim'; END IF; END $$;
SQL
echo 'Enterprise migration idempotency, opt-in defaults, constraints, and delivery uniqueness passed.'
