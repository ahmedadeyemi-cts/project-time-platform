-- Guarded rollback for migration 071. It refuses to discard encryption-key
-- metadata, probe evidence, or an active fenced lease.

BEGIN;

DO $projectpulse071_rollback_guard$
BEGIN
    IF EXISTS (SELECT 1 FROM ai_provider_secrets)
       OR EXISTS (SELECT 1 FROM ai_provider_secret_audit)
       OR EXISTS (SELECT 1 FROM ai_private_model_profiles WHERE endpoint_ciphertext IS NOT NULL OR token_ciphertext IS NOT NULL)
       OR EXISTS (SELECT 1 FROM ai_private_model_profile_audit)
       OR EXISTS (SELECT 1 FROM ai_provider_probe_evidence)
       OR EXISTS (
            SELECT 1
            FROM pulse_ai_document_processing_jobs
            WHERE lease_token IS NOT NULL OR job_status IN ('scanning','extracting','embedding','indexing','cancel_requested')
       ) THEN
        RAISE EXCEPTION 'Migration 071 rollback refused: encrypted configuration, key-rotation audit evidence, probe evidence, or worker lease state must be migrated or retired first.';
    END IF;
END;
$projectpulse071_rollback_guard$;

DROP INDEX IF EXISTS ix_pulse_ai_document_jobs_lease_fence;
ALTER TABLE pulse_ai_document_processing_jobs
    DROP COLUMN IF EXISTS lease_heartbeat_at,
    DROP COLUMN IF EXISTS lease_generation,
    DROP COLUMN IF EXISTS lease_token;

DROP TABLE IF EXISTS ai_provider_probe_evidence;

ALTER TABLE ai_provider_secrets
    DROP CONSTRAINT IF EXISTS ck_ai_provider_secrets_provider_code,
    DROP CONSTRAINT IF EXISTS ck_ai_provider_secret_nonce_length,
    DROP CONSTRAINT IF EXISTS ck_ai_provider_secret_tag_length;

ALTER TABLE ai_provider_settings
    DROP CONSTRAINT IF EXISTS ck_ai_provider_settings_provider_code;

ALTER TABLE ai_private_model_profiles
    DROP CONSTRAINT IF EXISTS ck_ai_private_profile_endpoint_nonce_length,
    DROP CONSTRAINT IF EXISTS ck_ai_private_profile_endpoint_tag_length,
    DROP CONSTRAINT IF EXISTS ck_ai_private_profile_token_nonce_length,
    DROP CONSTRAINT IF EXISTS ck_ai_private_profile_token_tag_length;

ALTER TABLE ai_private_model_profile_audit
    DROP COLUMN IF EXISTS previous_encryption_key_id,
    DROP COLUMN IF EXISTS encryption_key_id;
ALTER TABLE ai_private_model_profiles
    DROP COLUMN IF EXISTS token_encryption_key_id,
    DROP COLUMN IF EXISTS endpoint_encryption_key_id;
ALTER TABLE ai_provider_secret_audit
    DROP COLUMN IF EXISTS previous_encryption_key_id,
    DROP COLUMN IF EXISTS encryption_key_id;
ALTER TABLE ai_provider_secrets
    DROP COLUMN IF EXISTS encryption_key_id;

DELETE FROM schema_migrations WHERE migration_id = '071_ai_runtime_production_hardening';

COMMIT;
