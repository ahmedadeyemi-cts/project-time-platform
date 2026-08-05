#!/usr/bin/env bash
set -Eeuo pipefail

release_root="${1:-/opt/projectpulse/release}"
mode="${PROJECTPULSE_CELAR_MIGRATION_MODE:-verify}"
database_url="${PROJECTPULSE_DATABASE_URL:-}"
expected_release_sha="${PROJECTPULSE_EXPECTED_RELEASE_SHA:-}"
active_key_id="${PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_ID:-}"

fail() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }

[[ "$mode" == apply || "$mode" == verify ]] || fail "Migration mode must be apply or verify. Database rollback is intentionally unsupported."
[[ "$expected_release_sha" =~ ^[0-9a-f]{40}$ ]] || fail "PROJECTPULSE_EXPECTED_RELEASE_SHA must be an exact 40-character commit SHA."
[[ -n "$database_url" ]] || fail "PROJECTPULSE_DATABASE_URL is required."
[[ "$active_key_id" =~ ^[A-Za-z0-9][A-Za-z0-9_.-]{0,119}$ ]] || fail "A stable non-secret encryption key ID is required."
printf '::add-mask::%s\n' "$database_url"

cd "$release_root"
[[ "$(tr -d '[:space:]' < .projectpulse-release-commit)" == "$expected_release_sha" ]] ||
  fail "Migration image release identity does not match the authorized release."
sha256sum --check --strict database/migrations/SHA256SUMS

apply=false
[[ "$mode" == apply ]] && apply=true
has_071=false
[[ -f database/migrations/071_ai_runtime_production_hardening.sql ]] && has_071=true
has_070=false
[[ -f database/migrations/070_module_033_project_forge.sql ]] && has_070=true

# One psql session owns the advisory lock for the complete apply-and-verify
# window. No other Celar activation can interleave schema work.
psql "$database_url" -X --no-psqlrc --set=ON_ERROR_STOP=1 \
  --set="apply=$apply" --set="has_070=$has_070" --set="has_071=$has_071" \
  --set="active_key_id=$active_key_id" <<'SQL'
SELECT pg_advisory_lock(hashtextextended('projectpulse:celar-ai-production-activation', 0));

\if :apply
\ir database/migrations/052_document_intelligence_runtime.sql
\ir database/migrations/053_intelligence_answer_orchestration.sql
\ir database/migrations/061_celar_ai_capability_routing.sql
\if :has_071
\ir database/migrations/071_ai_runtime_production_hardening.sql
\endif
\endif

SELECT set_config('projectpulse.celar_has_071', :'has_071', false);
SELECT set_config('projectpulse.celar_has_070', :'has_070', false);
SELECT set_config('projectpulse.celar_active_key_id', :'active_key_id', false);
DO $celar_activation_invariants$
DECLARE
    missing TEXT[] := ARRAY[]::TEXT[];
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL THEN missing := array_append(missing, 'schema_migrations'); END IF;
    IF to_regclass('public.pulse_ai_document_processing_jobs') IS NULL THEN missing := array_append(missing, 'pulse_ai_document_processing_jobs'); END IF;
    IF to_regclass('public.pulse_ai_document_versions') IS NULL THEN missing := array_append(missing, 'pulse_ai_document_versions'); END IF;
    IF to_regclass('public.pulse_ai_document_chunks') IS NULL THEN missing := array_append(missing, 'pulse_ai_document_chunks'); END IF;
    IF to_regclass('public.pulse_ai_answer_runs') IS NULL THEN missing := array_append(missing, 'pulse_ai_answer_runs'); END IF;
    IF to_regclass('public.ai_capability_routes') IS NULL THEN missing := array_append(missing, 'ai_capability_routes'); END IF;
    IF to_regclass('public.ai_private_model_profiles') IS NULL THEN missing := array_append(missing, 'ai_private_model_profiles'); END IF;
    IF NOT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id = '052_pulse_ai_private_document_runtime') THEN missing := array_append(missing, 'migration-052'); END IF;
    IF NOT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id = '053_pulse_ai_private_rag_orchestration') THEN missing := array_append(missing, 'migration-053'); END IF;
    IF NOT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id = '061_celar_ai_capability_routing') THEN missing := array_append(missing, 'migration-061'); END IF;
    IF current_setting('projectpulse.celar_has_070', true)::BOOLEAN
       AND (to_regclass('public.project_forge_plans') IS NULL
            OR NOT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id = '070_module_033_project_forge')) THEN
        missing := array_append(missing, 'migration-070-registration');
    END IF;
    IF current_setting('projectpulse.celar_has_071', true)::BOOLEAN
       AND (to_regclass('public.ai_provider_probe_evidence') IS NULL
            OR NOT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id = '071_ai_runtime_production_hardening')) THEN
        missing := array_append(missing, 'migration-071');
    END IF;
    IF to_regclass('public.ai_provider_secrets') IS NOT NULL AND EXISTS (
        SELECT 1 FROM ai_provider_secrets
        WHERE encryption_key_id <> current_setting('projectpulse.celar_active_key_id', true)
    ) THEN
        missing := array_append(missing, 'provider-ciphertext-key-id');
    END IF;
    IF to_regclass('public.ai_private_model_profiles') IS NOT NULL AND EXISTS (
        SELECT 1 FROM ai_private_model_profiles
        WHERE (endpoint_ciphertext IS NOT NULL AND endpoint_encryption_key_id <> current_setting('projectpulse.celar_active_key_id', true))
           OR (token_ciphertext IS NOT NULL AND token_encryption_key_id <> current_setting('projectpulse.celar_active_key_id', true))
    ) THEN
        missing := array_append(missing, 'private-profile-ciphertext-key-id');
    END IF;
    IF cardinality(missing) > 0 THEN
        RAISE EXCEPTION 'Celar AI schema invariants failed: %', array_to_string(missing, ', ');
    END IF;
END;
$celar_activation_invariants$;

SELECT pg_advisory_unlock(hashtextextended('projectpulse:celar-ai-production-activation', 0));
SQL

printf 'CELAR_AI_REQUIRED_MIGRATIONS=VERIFIED\n'
