-- ProjectPulse Module 065 dual Microsoft connection support.
-- Additive only: preserves migration 045 Graph/services secrets and all existing Module 010/057/062 contracts.
BEGIN;

CREATE TABLE IF NOT EXISTS microsoft_integration_sso_client_secrets (
    environment_mode TEXT PRIMARY KEY,
    tenant_key TEXT NOT NULL,
    ciphertext BYTEA NOT NULL,
    nonce BYTEA NOT NULL,
    authentication_tag BYTEA NOT NULL,
    fingerprint_sha256 TEXT NOT NULL,
    encryption_key_source TEXT NOT NULL,
    updated_by_user_id UUID NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_microsoft_integration_sso_environment
        CHECK (environment_mode IN ('test', 'production'))
);

COMMENT ON TABLE microsoft_integration_sso_client_secrets IS
'Write-only SSO App Registration secrets. Microsoft services/Graph secrets remain in microsoft_integration_client_secrets for backward compatibility.';

DO $projectpulse046_registration$
BEGIN
    IF to_regclass('public.schema_migrations') IS NOT NULL THEN
        INSERT INTO schema_migrations (migration_id, description, applied_at)
        VALUES (
            '046_microsoft_sso_connection_profiles',
            'Add separate Test and Production SSO App Registration secret storage without changing existing Microsoft services, Graph, Module 010, Module 057, or Module 062 credentials',
            NOW()
        )
        ON CONFLICT (migration_id) DO UPDATE
        SET description = EXCLUDED.description,
            applied_at = EXCLUDED.applied_at;
    END IF;
END;
$projectpulse046_registration$;

COMMIT;
