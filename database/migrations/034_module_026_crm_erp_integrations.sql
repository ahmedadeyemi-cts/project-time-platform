-- ProjectPulse Module 026
-- Native CRM/ERP connection registry, encrypted credential metadata, OAuth state,
-- availability checks, and audit evidence. This migration creates source only;
-- applying it remains a separately governed database action.

BEGIN;

ALTER TABLE crm_integration_providers
    ADD COLUMN IF NOT EXISTS base_url TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS health_check_url TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS oauth_authorization_url TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS oauth_token_url TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS oauth_client_id TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS oauth_scopes TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS api_key_header TEXT NOT NULL DEFAULT 'Authorization',
    ADD COLUMN IF NOT EXISTS api_key_prefix TEXT NOT NULL DEFAULT 'Bearer',
    ADD COLUMN IF NOT EXISTS is_builtin BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS is_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS availability_status TEXT NOT NULL DEFAULT 'not_configured',
    ADD COLUMN IF NOT EXISTS last_checked_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS last_available_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS last_status_code INTEGER,
    ADD COLUMN IF NOT EXISTS last_error_code TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS created_by UUID REFERENCES app_users(user_id),
    ADD COLUMN IF NOT EXISTS updated_by UUID REFERENCES app_users(user_id);

ALTER TABLE crm_integration_providers
    DROP CONSTRAINT IF EXISTS crm_integration_providers_auth_model_check;

UPDATE crm_integration_providers
SET auth_model = CASE
    WHEN auth_model ILIKE '%api%' AND auth_model NOT ILIKE '%oauth%' THEN 'api_key'
    ELSE 'oauth2'
END
WHERE auth_model NOT IN ('api_key', 'oauth2');

ALTER TABLE crm_integration_providers
    ADD CONSTRAINT crm_integration_providers_auth_model_check
    CHECK (auth_model IN ('api_key', 'oauth2'));

ALTER TABLE crm_integration_providers
    DROP CONSTRAINT IF EXISTS crm_integration_providers_availability_status_check;

ALTER TABLE crm_integration_providers
    ADD CONSTRAINT crm_integration_providers_availability_status_check
    CHECK (availability_status IN ('not_configured', 'available', 'authentication_failed', 'unavailable', 'disabled'));

CREATE TABLE IF NOT EXISTS crm_integration_credentials (
    provider_key TEXT NOT NULL REFERENCES crm_integration_providers(provider_key) ON DELETE CASCADE,
    credential_kind TEXT NOT NULL CHECK (credential_kind IN ('api_key', 'oauth_client_secret', 'oauth_token')),
    ciphertext BYTEA NOT NULL,
    nonce BYTEA NOT NULL,
    authentication_tag BYTEA NOT NULL,
    credential_version TEXT NOT NULL,
    expires_at TIMESTAMPTZ,
    rotated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    rotated_by UUID NOT NULL REFERENCES app_users(user_id),
    PRIMARY KEY (provider_key, credential_kind)
);

CREATE TABLE IF NOT EXISTS crm_integration_oauth_states (
    state_hash TEXT PRIMARY KEY,
    provider_key TEXT NOT NULL REFERENCES crm_integration_providers(provider_key) ON DELETE CASCADE,
    actor_user_id UUID NOT NULL REFERENCES app_users(user_id),
    redirect_uri TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMPTZ NOT NULL,
    used_at TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS crm_integration_connection_checks (
    connection_check_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_key TEXT NOT NULL REFERENCES crm_integration_providers(provider_key) ON DELETE CASCADE,
    availability_status TEXT NOT NULL,
    http_status_code INTEGER,
    duration_ms INTEGER NOT NULL,
    error_code TEXT NOT NULL DEFAULT '',
    checked_by UUID NOT NULL REFERENCES app_users(user_id),
    checked_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT crm_integration_connection_checks_status_check
        CHECK (availability_status IN ('available', 'authentication_failed', 'unavailable'))
);

CREATE INDEX IF NOT EXISTS idx_crm_integration_connection_checks_provider
    ON crm_integration_connection_checks(provider_key, checked_at DESC);

CREATE INDEX IF NOT EXISTS idx_crm_integration_oauth_states_expiry
    ON crm_integration_oauth_states(expires_at)
    WHERE used_at IS NULL;

CREATE TABLE IF NOT EXISTS projectpulse_module_audit_events (
    event_id UUID PRIMARY KEY,
    module_number TEXT NOT NULL,
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    action_code TEXT NOT NULL,
    actor_user_id UUID NOT NULL REFERENCES app_users(user_id),
    evidence_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO crm_integration_providers (
    provider_key,
    provider_name,
    provider_type,
    provider_status,
    auth_model,
    configuration_scope,
    secret_storage_policy,
    notes,
    is_builtin
)
VALUES
    ('zendesk_sell', 'SELL (Zendesk Sell)', 'crm', 'native_configuration', 'oauth2', 'server_side_only', 'encrypted_write_only', 'Sales and commercial source for project names, quotes, pricing, and rate review.', TRUE),
    ('salesforce', 'Salesforce', 'crm', 'native_configuration', 'oauth2', 'server_side_only', 'encrypted_write_only', 'Salesforce connected-app integration.', TRUE),
    ('certinia', 'Certinia', 'erp_psa', 'native_configuration', 'oauth2', 'server_side_only', 'encrypted_write_only', 'Certinia integration through an approved Salesforce connected app.', TRUE),
    ('servicenow', 'ServiceNow', 'itsm_erp', 'native_configuration', 'oauth2', 'server_side_only', 'encrypted_write_only', 'ServiceNow instance integration.', TRUE)
ON CONFLICT (provider_key) DO UPDATE
SET provider_name = EXCLUDED.provider_name,
    provider_type = EXCLUDED.provider_type,
    provider_status = EXCLUDED.provider_status,
    configuration_scope = EXCLUDED.configuration_scope,
    secret_storage_policy = EXCLUDED.secret_storage_policy,
    notes = EXCLUDED.notes,
    is_builtin = TRUE,
    updated_at = NOW();

INSERT INTO app_permissions (permission_code, permission_name, module_code, permission_description)
VALUES
    ('VIEW_INTEGRATIONS_026', 'View CRM/ERP integration status', '026', 'View configured CRM/ERP providers and sanitized availability status.'),
    ('MANAGE_INTEGRATIONS_026', 'Manage CRM/ERP integrations', '026', 'Add providers, configure OAuth or API-key metadata, replace write-only credentials, and run connection tests.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code = 'VIEW_INTEGRATIONS_026'
WHERE upper(r.role_code) IN (
    'SUPER_ADMINISTRATOR', 'ADMINISTRATOR', 'INTEGRATION_ADMINISTRATOR',
    'PROJECT_TEAM_COORDINATOR', 'PROJECT_COORDINATOR',
    'SALES', 'ACCOUNT_EXECUTIVE', 'ACCOUNT_EXECUTIVES', 'INSIDE_SALES',
    'SOLUTION_ARCHITECT', 'SA', 'SAA'
)
ON CONFLICT DO NOTHING;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code = 'MANAGE_INTEGRATIONS_026'
WHERE upper(r.role_code) IN ('SUPER_ADMINISTRATOR', 'ADMINISTRATOR', 'INTEGRATION_ADMINISTRATOR')
ON CONFLICT DO NOTHING;

INSERT INTO app_feature_catalog (
    feature_code,
    feature_name,
    module_code,
    route_anchor,
    required_permission_code,
    feature_description,
    display_order
)
VALUES (
    'CRM_ERP_INTEGRATIONS_026',
    'CRM/ERP Integration Control Center',
    '026',
    '#crm-integration',
    'VIEW_INTEGRATIONS_026',
    'Configure SELL, Salesforce, Certinia, ServiceNow, and manually registered CRM/ERP platforms and review sanitized availability.',
    260
)
ON CONFLICT (feature_code) DO UPDATE
SET feature_name = EXCLUDED.feature_name,
    module_code = EXCLUDED.module_code,
    route_anchor = EXCLUDED.route_anchor,
    required_permission_code = EXCLUDED.required_permission_code,
    feature_description = EXCLUDED.feature_description,
    display_order = EXCLUDED.display_order,
    is_active = TRUE,
    updated_at = NOW();

COMMIT;
