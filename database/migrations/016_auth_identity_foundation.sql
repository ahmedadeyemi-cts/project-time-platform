-- Project Health Dashboard
-- Migration: 016_auth_identity_foundation.sql
-- Purpose: Authentication and identity foundation for Entra SSO, local admin login, and password reset approval workflow.

BEGIN;

CREATE TABLE IF NOT EXISTS auth_identity_providers (
    auth_identity_provider_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_code VARCHAR(75) NOT NULL UNIQUE,
    provider_name VARCHAR(200) NOT NULL,
    provider_type VARCHAR(50) NOT NULL,
    authority_url TEXT,
    tenant_id TEXT,
    client_id TEXT,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS auth_local_accounts (
    auth_local_account_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL UNIQUE REFERENCES app_users(user_id) ON DELETE CASCADE,
    username VARCHAR(255) NOT NULL UNIQUE,
    password_hash TEXT,
    password_hash_algorithm VARCHAR(100) NOT NULL DEFAULT 'ASP.NET_CORE_PASSWORD_HASHER',
    password_set_at TIMESTAMPTZ,
    must_change_password BOOLEAN NOT NULL DEFAULT TRUE,
    failed_login_count INTEGER NOT NULL DEFAULT 0,
    locked_until TIMESTAMPTZ,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS auth_external_identity_links (
    auth_external_identity_link_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    provider_code VARCHAR(75) NOT NULL,
    external_subject_id TEXT,
    user_principal_name VARCHAR(255),
    email VARCHAR(255),
    display_name VARCHAR(255),
    raw_claims JSONB,
    last_login_at TIMESTAMPTZ,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(provider_code, external_subject_id),
    UNIQUE(provider_code, user_principal_name)
);

CREATE TABLE IF NOT EXISTS auth_password_reset_requests (
    auth_password_reset_request_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    requested_by_email VARCHAR(255) NOT NULL,
    approval_email_to TEXT[] NOT NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'pending_approval',
    reset_token_hash TEXT,
    requested_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    approved_at TIMESTAMPTZ,
    approved_by_email VARCHAR(255),
    expires_at TIMESTAMPTZ,
    completed_at TIMESTAMPTZ,
    notes TEXT
);

CREATE TABLE IF NOT EXISTS auth_login_events (
    auth_login_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID REFERENCES app_users(user_id),
    username VARCHAR(255),
    login_method VARCHAR(50) NOT NULL,
    login_result VARCHAR(50) NOT NULL,
    source_ip VARCHAR(100),
    user_agent TEXT,
    event_details JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS notification_outbox (
    notification_outbox_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    notification_type VARCHAR(100) NOT NULL,
    recipient_email VARCHAR(255) NOT NULL,
    cc_email TEXT[],
    subject TEXT NOT NULL,
    body TEXT NOT NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'pending',
    related_entity_type VARCHAR(100),
    related_entity_id UUID,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    sent_at TIMESTAMPTZ,
    error_message TEXT
);

INSERT INTO auth_identity_providers (provider_code, provider_name, provider_type, authority_url, is_active)
VALUES
    ('ENTRA_ID', 'Microsoft Entra ID', 'oidc', 'https://login.microsoftonline.com/{tenant}/v2.0', TRUE),
    ('LOCAL', 'Project Health Dashboard Local Authentication', 'local', NULL, TRUE)
ON CONFLICT (provider_code) DO UPDATE
SET provider_name = EXCLUDED.provider_name,
    provider_type = EXCLUDED.provider_type,
    authority_url = EXCLUDED.authority_url,
    is_active = TRUE,
    updated_at = NOW();

-- Local break-glass administrator account.
INSERT INTO auth_local_accounts (user_id, username, must_change_password, is_active)
SELECT user_id, email, TRUE, TRUE
FROM app_users
WHERE email = 'ahmed.adeyemi@ussignal.local'
ON CONFLICT (username) DO UPDATE
SET user_id = EXCLUDED.user_id,
    must_change_password = TRUE,
    is_active = TRUE,
    updated_at = NOW();

-- SSO identity placeholders for known ussignal.com users.
INSERT INTO auth_external_identity_links (user_id, provider_code, user_principal_name, email, display_name, is_active)
SELECT user_id, 'ENTRA_ID', email, email, display_name, TRUE
FROM app_users
WHERE email ILIKE '%@ussignal.com'
ON CONFLICT (provider_code, user_principal_name) DO UPDATE
SET user_id = EXCLUDED.user_id,
    email = EXCLUDED.email,
    display_name = EXCLUDED.display_name,
    is_active = TRUE,
    updated_at = NOW();

INSERT INTO schema_migrations (migration_id, description)
VALUES ('016_auth_identity_foundation', 'Authentication and identity foundation for Entra SSO and local admin login')
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
