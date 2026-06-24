BEGIN;

CREATE TABLE IF NOT EXISTS auth_sessions (
    auth_session_id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES app_users(user_id),
    provider_code TEXT NOT NULL,
    session_token_hash TEXT NOT NULL UNIQUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMPTZ NOT NULL,
    revoked_at TIMESTAMPTZ NULL,
    revoked_reason TEXT NULL,
    ip_address TEXT NULL,
    user_agent TEXT NULL,
    session_window_minutes INTEGER NOT NULL DEFAULT 120
);

CREATE INDEX IF NOT EXISTS idx_auth_sessions_user_id
ON auth_sessions(user_id);

CREATE INDEX IF NOT EXISTS idx_auth_sessions_token_hash
ON auth_sessions(session_token_hash);

CREATE INDEX IF NOT EXISTS idx_auth_sessions_active
ON auth_sessions(expires_at, revoked_at);

ALTER TABLE auth_local_accounts
ADD COLUMN IF NOT EXISTS password_hash_updated_at TIMESTAMPTZ NULL;

ALTER TABLE auth_local_accounts
ADD COLUMN IF NOT EXISTS last_password_change_at TIMESTAMPTZ NULL;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '019_local_password_sessions',
    'Local password hashing and API session enforcement foundation',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
