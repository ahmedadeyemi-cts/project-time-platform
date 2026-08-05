-- ProjectPulse migration 071
-- Production hardening for Module 064 provider secrets/private profile state and
-- Module 011 private document worker ownership. This migration is additive and
-- contains no secret values.

BEGIN;

DO $projectpulse071_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.ai_capability_routes') IS NULL
       OR to_regclass('public.ai_private_model_profiles') IS NULL
       OR to_regclass('public.pulse_ai_document_processing_jobs') IS NULL THEN
        RAISE EXCEPTION 'Migration 071 requires migrations 052, 053, 061, and the canonical schema migration ledger.';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id = '052_pulse_ai_private_document_runtime'
    ) OR NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id = '053_pulse_ai_private_rag_orchestration'
    ) OR NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id = '061_celar_ai_capability_routing'
    ) THEN
        RAISE EXCEPTION 'Migration 071 requires applied migration ledger entries 052, 053, and 061.';
    END IF;
END;
$projectpulse071_prerequisites$;

-- These tables were historically created by the API at runtime. Migration 071
-- makes the release runner the only schema owner; application startup now only
-- validates this schema.
CREATE TABLE IF NOT EXISTS ai_provider_secrets (
    provider_code TEXT PRIMARY KEY,
    ciphertext BYTEA NOT NULL,
    nonce BYTEA NOT NULL,
    tag BYTEA NOT NULL,
    encryption_key_id VARCHAR(120) NOT NULL DEFAULT 'legacy-v1',
    version TEXT NOT NULL,
    rotated_at TIMESTAMPTZ NOT NULL,
    rotated_by UUID NOT NULL,
    CONSTRAINT ck_ai_provider_secrets_provider_code
        CHECK (provider_code IN ('claude','openai'))
);

CREATE TABLE IF NOT EXISTS ai_provider_secret_audit (
    audit_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    provider_code TEXT NOT NULL,
    action TEXT NOT NULL,
    version TEXT NOT NULL,
    encryption_key_id VARCHAR(120) NOT NULL DEFAULT 'legacy-v1',
    previous_encryption_key_id VARCHAR(120) NULL,
    actor_user_id UUID NOT NULL,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS ai_provider_settings (
    provider_code TEXT PRIMARY KEY,
    model TEXT NOT NULL,
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_by UUID NOT NULL,
    CONSTRAINT ck_ai_provider_settings_provider_code
        CHECK (provider_code IN ('claude','openai'))
);

CREATE TABLE IF NOT EXISTS ai_provider_settings_audit (
    audit_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    provider_code TEXT NOT NULL,
    action TEXT NOT NULL,
    model TEXT NOT NULL,
    actor_user_id UUID NOT NULL,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

ALTER TABLE ai_provider_secrets
    ADD COLUMN IF NOT EXISTS encryption_key_id VARCHAR(120) NOT NULL DEFAULT 'legacy-v1';
ALTER TABLE ai_provider_secret_audit
    ADD COLUMN IF NOT EXISTS encryption_key_id VARCHAR(120) NOT NULL DEFAULT 'legacy-v1',
    ADD COLUMN IF NOT EXISTS previous_encryption_key_id VARCHAR(120) NULL;
ALTER TABLE ai_provider_settings
    ADD COLUMN IF NOT EXISTS enabled BOOLEAN NOT NULL DEFAULT TRUE;

DO $projectpulse071_provider_cipher_constraints$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_ai_provider_secrets_provider_code'
          AND conrelid = 'public.ai_provider_secrets'::regclass
    ) THEN
        ALTER TABLE ai_provider_secrets
            ADD CONSTRAINT ck_ai_provider_secrets_provider_code
            CHECK (provider_code IN ('claude','openai')) NOT VALID;
        ALTER TABLE ai_provider_secrets
            VALIDATE CONSTRAINT ck_ai_provider_secrets_provider_code;
    END IF;
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_ai_provider_settings_provider_code'
          AND conrelid = 'public.ai_provider_settings'::regclass
    ) THEN
        ALTER TABLE ai_provider_settings
            ADD CONSTRAINT ck_ai_provider_settings_provider_code
            CHECK (provider_code IN ('claude','openai')) NOT VALID;
        ALTER TABLE ai_provider_settings
            VALIDATE CONSTRAINT ck_ai_provider_settings_provider_code;
    END IF;
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_ai_provider_secret_nonce_length'
          AND conrelid = 'public.ai_provider_secrets'::regclass
    ) THEN
        ALTER TABLE ai_provider_secrets
            ADD CONSTRAINT ck_ai_provider_secret_nonce_length CHECK (octet_length(nonce) = 12) NOT VALID;
        ALTER TABLE ai_provider_secrets VALIDATE CONSTRAINT ck_ai_provider_secret_nonce_length;
    END IF;
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_ai_provider_secret_tag_length'
          AND conrelid = 'public.ai_provider_secrets'::regclass
    ) THEN
        ALTER TABLE ai_provider_secrets
            ADD CONSTRAINT ck_ai_provider_secret_tag_length CHECK (octet_length(tag) = 16) NOT VALID;
        ALTER TABLE ai_provider_secrets VALIDATE CONSTRAINT ck_ai_provider_secret_tag_length;
    END IF;
END;
$projectpulse071_provider_cipher_constraints$;

ALTER TABLE ai_private_model_profiles
    ADD COLUMN IF NOT EXISTS endpoint_encryption_key_id VARCHAR(120) NOT NULL DEFAULT 'legacy-v1',
    ADD COLUMN IF NOT EXISTS token_encryption_key_id VARCHAR(120) NOT NULL DEFAULT 'legacy-v1';
ALTER TABLE ai_private_model_profile_audit
    ADD COLUMN IF NOT EXISTS encryption_key_id VARCHAR(120) NOT NULL DEFAULT 'legacy-v1',
    ADD COLUMN IF NOT EXISTS previous_encryption_key_id VARCHAR(120) NULL;

DO $projectpulse071_private_profile_cipher_constraints$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_ai_private_profile_endpoint_nonce_length'
          AND conrelid = 'public.ai_private_model_profiles'::regclass
    ) THEN
        ALTER TABLE ai_private_model_profiles
            ADD CONSTRAINT ck_ai_private_profile_endpoint_nonce_length
            CHECK (endpoint_nonce IS NULL OR octet_length(endpoint_nonce) = 12) NOT VALID;
        ALTER TABLE ai_private_model_profiles
            VALIDATE CONSTRAINT ck_ai_private_profile_endpoint_nonce_length;
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_ai_private_profile_endpoint_tag_length'
          AND conrelid = 'public.ai_private_model_profiles'::regclass
    ) THEN
        ALTER TABLE ai_private_model_profiles
            ADD CONSTRAINT ck_ai_private_profile_endpoint_tag_length
            CHECK (endpoint_tag IS NULL OR octet_length(endpoint_tag) = 16) NOT VALID;
        ALTER TABLE ai_private_model_profiles
            VALIDATE CONSTRAINT ck_ai_private_profile_endpoint_tag_length;
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_ai_private_profile_token_nonce_length'
          AND conrelid = 'public.ai_private_model_profiles'::regclass
    ) THEN
        ALTER TABLE ai_private_model_profiles
            ADD CONSTRAINT ck_ai_private_profile_token_nonce_length
            CHECK (token_nonce IS NULL OR octet_length(token_nonce) = 12) NOT VALID;
        ALTER TABLE ai_private_model_profiles
            VALIDATE CONSTRAINT ck_ai_private_profile_token_nonce_length;
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_ai_private_profile_token_tag_length'
          AND conrelid = 'public.ai_private_model_profiles'::regclass
    ) THEN
        ALTER TABLE ai_private_model_profiles
            ADD CONSTRAINT ck_ai_private_profile_token_tag_length
            CHECK (token_tag IS NULL OR octet_length(token_tag) = 16) NOT VALID;
        ALTER TABLE ai_private_model_profiles
            VALIDATE CONSTRAINT ck_ai_private_profile_token_tag_length;
    END IF;
END;
$projectpulse071_private_profile_cipher_constraints$;

-- Probe evidence is shared by every API replica and is tied to the exact
-- private-profile revision. A profile change invalidates old evidence without a
-- destructive delete. Readiness accepts only unexpired successful evidence.
CREATE TABLE IF NOT EXISTS ai_provider_probe_evidence (
    probe_evidence_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_code VARCHAR(120) NOT NULL,
    environment_code VARCHAR(80) NOT NULL,
    profile_revision INTEGER NOT NULL CHECK (profile_revision >= 0),
    available BOOLEAN NOT NULL,
    diagnostic_code VARCHAR(120) NOT NULL,
    request_id VARCHAR(240) NOT NULL DEFAULT '',
    model_fingerprint VARCHAR(64) NOT NULL DEFAULT '',
    replica_id VARCHAR(200) NOT NULL,
    tested_at TIMESTAMPTZ NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_ai_provider_probe_evidence_ttl CHECK (expires_at > tested_at)
);

CREATE INDEX IF NOT EXISTS ix_ai_provider_probe_evidence_current
    ON ai_provider_probe_evidence(environment_code, provider_code, profile_revision, tested_at DESC);
CREATE INDEX IF NOT EXISTS ix_ai_provider_probe_evidence_expiry
    ON ai_provider_probe_evidence(expires_at);

-- A random token plus monotonically increasing generation fences every claim.
-- Heartbeats may extend only the current token/generation; stale replicas cannot
-- advance, complete, or publish an old job after ownership changes.
ALTER TABLE pulse_ai_document_processing_jobs
    ADD COLUMN IF NOT EXISTS lease_token UUID NULL,
    ADD COLUMN IF NOT EXISTS lease_generation BIGINT NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS lease_heartbeat_at TIMESTAMPTZ NULL;

CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_jobs_lease_fence
    ON pulse_ai_document_processing_jobs(
        pulse_ai_document_processing_job_id,
        lease_owner,
        lease_token,
        lease_generation
    )
    WHERE lease_token IS NOT NULL;

COMMENT ON COLUMN ai_provider_secrets.encryption_key_id IS
    'Non-secret key-ring identifier used to select the AES-256-GCM key; key material is never stored in PostgreSQL.';
COMMENT ON TABLE ai_provider_probe_evidence IS
    'Cross-replica, revision-bound provider probe evidence. No endpoint, bearer token, prompt, response, or document text is stored.';
COMMENT ON COLUMN pulse_ai_document_processing_jobs.lease_token IS
    'Random per-claim fencing token; all worker-owned writes must match token and generation.';

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '071_ai_runtime_production_hardening',
    'Migration-owned Module 064 schemas, version-aware encryption key IDs, shared probe evidence, and fenced private-document worker leases',
    NOW()
)
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
