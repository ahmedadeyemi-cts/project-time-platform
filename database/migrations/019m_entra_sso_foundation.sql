BEGIN;

CREATE TABLE IF NOT EXISTS auth_sso_state (
    auth_sso_state_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    state_token TEXT NOT NULL UNIQUE,
    nonce_token TEXT NOT NULL,
    provider_code TEXT NOT NULL DEFAULT 'ENTRA_ID',
    redirect_uri TEXT NOT NULL,
    requested_email TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMPTZ NOT NULL,
    consumed_at TIMESTAMPTZ NULL,
    client_ip TEXT NULL,
    user_agent TEXT NULL
);

CREATE INDEX IF NOT EXISTS ix_auth_sso_state_state_token
ON auth_sso_state(state_token);

CREATE INDEX IF NOT EXISTS ix_auth_sso_state_expires
ON auth_sso_state(expires_at, consumed_at);

ALTER TABLE app_users
ADD COLUMN IF NOT EXISTS entra_tenant_id TEXT NULL;

ALTER TABLE app_users
ADD COLUMN IF NOT EXISTS entra_user_principal_name TEXT NULL;

ALTER TABLE app_users
ADD COLUMN IF NOT EXISTS last_sso_login_at TIMESTAMPTZ NULL;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '019m_entra_sso_foundation',
    'Real Microsoft Entra SSO authorization-code foundation for test tenant validation',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
