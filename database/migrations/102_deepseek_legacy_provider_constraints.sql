BEGIN;
SET LOCAL lock_timeout = '10s';
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid='ai_provider_secrets'::regclass
        AND conname='ck_ai_provider_secrets_provider_code' AND convalidated
        AND pg_get_constraintdef(oid) LIKE '%deepseek_v4%')
       OR NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid='ai_provider_settings'::regclass
        AND conname='ck_ai_provider_settings_provider_code' AND convalidated
        AND pg_get_constraintdef(oid) LIKE '%deepseek_v4%') THEN
        RAISE EXCEPTION 'Validated migration 101 provider constraints are required';
    END IF;
END $$;
-- Early API-created tables retained these automatically named constraints.
-- The validated, named migration-101 constraints remain authoritative.
ALTER TABLE ai_provider_secrets DROP CONSTRAINT IF EXISTS ai_provider_secrets_provider_code_check;
ALTER TABLE ai_provider_settings DROP CONSTRAINT IF EXISTS ai_provider_settings_provider_code_check;
INSERT INTO schema_migrations(migration_id,description)
VALUES ('102_deepseek_legacy_provider_constraints','Remove obsolete provider checks while preserving validated provider and encryption constraints')
ON CONFLICT (migration_id) DO NOTHING;
COMMIT;
