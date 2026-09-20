#!/usr/bin/env bash
set -euo pipefail
: "${ENTERPRISE_COMPLETION_DATABASE_URL:?Use a disposable local enterprise_completion_test database}"
[[ "$ENTERPRISE_COMPLETION_DATABASE_URL" =~ ^postgres(ql)?://[^/]+@(localhost|127\.0\.0\.1):[0-9]+/enterprise_completion_test$ ]] || { echo 'Disposable local test database required'; exit 1; }
psql "$ENTERPRISE_COMPLETION_DATABASE_URL" -X -v ON_ERROR_STOP=1 <<'SQL'
CREATE TABLE schema_migrations(migration_id text PRIMARY KEY, description text NOT NULL);
CREATE TABLE ai_provider_secrets(provider_code text PRIMARY KEY CONSTRAINT ck_ai_provider_secrets_provider_code CHECK(provider_code IN ('deepseek_v4','claude','openai')));
CREATE TABLE ai_provider_settings(provider_code text PRIMARY KEY CONSTRAINT ck_ai_provider_settings_provider_code CHECK(provider_code IN ('deepseek_v4','claude','openai')));
CREATE TABLE ai_capability_routes(feature_code text, route_targets jsonb, external_context_policy text, revision int, updated_at timestamptz);
CREATE TABLE ai_capability_route_audit(feature_code text, previous_targets jsonb, new_targets jsonb, previous_external_context_policy text, new_external_context_policy text, actor_user_id uuid);
CREATE TABLE ai_private_model_profiles(environment_code text PRIMARY KEY, revision int NOT NULL DEFAULT 1);
INSERT INTO ai_private_model_profiles VALUES('test',4);
SQL
# Execute the same ordered provider block embedded in the deployment image.
replay_providers() {
  local provider_block
  provider_block="$(python3 - <<'PYBLOCK'
from pathlib import Path
s=Path('scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh').read_text()
a=s.index('# Never replay the older, narrower constraint')
b=s.index("echo 'MIGRATION_101_DEEPSEEK_V4=APPLIED_AND_VERIFIED'",a)
print(s[a:b])
PYBLOCK
)"
  ROOT="$PWD"
  psql() { command psql "$ENTERPRISE_COMPLETION_DATABASE_URL" "$@"; }
  eval "$provider_block"
  unset -f psql
}
replay_providers
for attempt in 1 2; do
  for migration in database/migrations/{112_optional_ai_providers,113_module065_teams_notifications,114_module064_private_admin_override}.sql; do
    psql "$ENTERPRISE_COMPLETION_DATABASE_URL" -X -v ON_ERROR_STOP=1 -f "$migration" >/dev/null
  done
done
# Reproduce the old deployed order: 101 narrows constraints despite ledgered 112.
psql "$ENTERPRISE_COMPLETION_DATABASE_URL" -X -v ON_ERROR_STOP=1 -f database/migrations/101_deepseek_v4_provider.sql >/dev/null
replay_providers
psql "$ENTERPRISE_COMPLETION_DATABASE_URL" -X -v ON_ERROR_STOP=1 <<'SQL'
INSERT INTO ai_provider_secrets VALUES('gemini'),('copilot_studio');
INSERT INTO ai_provider_settings VALUES('gemini'),('copilot_studio');
INSERT INTO module065_teams_configuration(environment,updated_by) VALUES('test','00000000-0000-0000-0000-000000000001');
DO $$ BEGIN
 IF (SELECT count(*) FROM schema_migrations) <> 4 THEN RAISE EXCEPTION 'Migration idempotency failed'; END IF;
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

# Once optional credentials exist, replay must neither reject nor remove them.
replay_providers
psql "$ENTERPRISE_COMPLETION_DATABASE_URL" -X -v ON_ERROR_STOP=1 <<'SQL'
DO $$ BEGIN
 IF (SELECT count(*) FROM ai_provider_secrets WHERE provider_code IN ('gemini','copilot_studio')) <> 2 THEN RAISE EXCEPTION 'Optional secrets lost after deployment replay'; END IF;
 BEGIN INSERT INTO ai_provider_secrets VALUES('unknown'); RAISE EXCEPTION 'Unknown provider accepted'; EXCEPTION WHEN check_violation THEN NULL; END;
END $$;
SQL
