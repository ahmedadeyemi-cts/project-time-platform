BEGIN;

-- Module 064 remains the single AI provider and capability-routing boundary.
-- Celar AI is a private orchestration target, not a public vendor provider.
CREATE TABLE IF NOT EXISTS ai_capability_routes (
    feature_code TEXT PRIMARY KEY,
    route_targets JSONB NOT NULL,
    external_context_policy TEXT NOT NULL,
    revision INTEGER NOT NULL DEFAULT 1 CHECK (revision >= 1),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_by UUID NULL
);

CREATE TABLE IF NOT EXISTS ai_capability_route_audit (
    audit_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    feature_code TEXT NOT NULL,
    previous_targets JSONB NULL,
    new_targets JSONB NOT NULL,
    previous_external_context_policy TEXT NULL,
    new_external_context_policy TEXT NOT NULL,
    actor_user_id UUID NULL,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS ix_ai_capability_route_audit_feature_occurred
    ON ai_capability_route_audit (feature_code, occurred_at DESC);

-- Endpoint and bearer-token values are encrypted with the existing Module 064
-- AES-GCM key boundary. GET APIs return only fingerprints and readiness flags.
CREATE TABLE IF NOT EXISTS ai_private_model_profiles (
    environment_code TEXT PRIMARY KEY,
    enabled BOOLEAN NOT NULL DEFAULT FALSE,
    endpoint_ciphertext BYTEA NULL,
    endpoint_nonce BYTEA NULL,
    endpoint_tag BYTEA NULL,
    endpoint_host_fingerprint TEXT NULL,
    model_name TEXT NOT NULL DEFAULT '',
    auth_mode TEXT NOT NULL DEFAULT 'bearer',
    token_ciphertext BYTEA NULL,
    token_nonce BYTEA NULL,
    token_tag BYTEA NULL,
    token_fingerprint TEXT NULL,
    private_host_allowlist JSONB NOT NULL DEFAULT '[]'::jsonb,
    require_private_model_for_documents BOOLEAN NOT NULL DEFAULT TRUE,
    revision INTEGER NOT NULL DEFAULT 1 CHECK (revision >= 1),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_by UUID NULL,
    CONSTRAINT ck_ai_private_model_endpoint_cipher_bundle CHECK (
        (endpoint_ciphertext IS NULL AND endpoint_nonce IS NULL AND endpoint_tag IS NULL)
        OR
        (endpoint_ciphertext IS NOT NULL AND endpoint_nonce IS NOT NULL AND endpoint_tag IS NOT NULL)
    ),
    CONSTRAINT ck_ai_private_model_token_cipher_bundle CHECK (
        (token_ciphertext IS NULL AND token_nonce IS NULL AND token_tag IS NULL)
        OR
        (token_ciphertext IS NOT NULL AND token_nonce IS NOT NULL AND token_tag IS NOT NULL)
    )
);

CREATE TABLE IF NOT EXISTS ai_private_model_profile_audit (
    audit_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    environment_code TEXT NOT NULL,
    action TEXT NOT NULL,
    revision INTEGER NOT NULL,
    actor_user_id UUID NULL,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS ix_ai_private_model_profile_audit_environment_occurred
    ON ai_private_model_profile_audit (environment_code, occurred_at DESC);

-- Default routing is Celar AI first, optional Claude and OpenAI second/third,
-- and the deterministic governed local template last. Route order never weakens
-- the data classification and DLP policy.
INSERT INTO ai_capability_routes
    (feature_code, route_targets, external_context_policy, revision, updated_at, updated_by)
VALUES
    ('timesheet_non_project_description', '["celar_ai","claude","openai","local_template"]'::jsonb, 'sanitized_non_document_context_only', 1, CURRENT_TIMESTAMP, NULL),
    ('timesheet_project_task_description', '["celar_ai","claude","openai","local_template"]'::jsonb, 'sanitized_generic_only', 1, CURRENT_TIMESTAMP, NULL),
    ('timesheet_service_request_description', '["celar_ai","claude","openai","local_template"]'::jsonb, 'sanitized_generic_only', 1, CURRENT_TIMESTAMP, NULL),
    ('sow_gsd_planning', '["celar_ai","claude","openai","local_template"]'::jsonb, 'sanitized_generic_only', 1, CURRENT_TIMESTAMP, NULL),
    ('project_flowhive_plan', '["celar_ai","claude","openai","local_template"]'::jsonb, 'sanitized_generic_only', 1, CURRENT_TIMESTAMP, NULL),
    ('closeout_communication', '["celar_ai","claude","openai","local_template"]'::jsonb, 'sanitized_generic_only', 1, CURRENT_TIMESTAMP, NULL),
    ('help_assistant', '["celar_ai","claude","openai","local_template"]'::jsonb, 'sanitized_generic_only', 1, CURRENT_TIMESTAMP, NULL)
ON CONFLICT (feature_code) DO NOTHING;

COMMENT ON TABLE ai_capability_routes IS
    'Module 064 ordered Celar AI, Claude, OpenAI, and governed-local routes by business capability.';
COMMENT ON TABLE ai_private_model_profiles IS
    'Write-only encrypted private Celar AI inference profile by environment; secret and endpoint values are never returned.';

COMMIT;
