-- ProjectPulse Modules 010/065 Microsoft Integration consolidation.
-- Additive only: preserves Module 067 configuration, APIs, secrets, and audit history.
BEGIN;

CREATE TABLE IF NOT EXISTS microsoft_integration_client_secrets (
    tenant_key TEXT PRIMARY KEY,
    ciphertext BYTEA NOT NULL,
    nonce BYTEA NOT NULL,
    authentication_tag BYTEA NOT NULL,
    fingerprint_sha256 TEXT NOT NULL,
    encryption_key_source TEXT NOT NULL,
    updated_by_user_id UUID NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS microsoft_integration_audit_events (
    microsoft_integration_audit_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    actor_user_id UUID NULL,
    actor_email TEXT NOT NULL DEFAULT '',
    action_code TEXT NOT NULL,
    tenant_key TEXT NOT NULL DEFAULT '',
    outcome_code TEXT NOT NULL,
    correlation_id TEXT NOT NULL DEFAULT '',
    event_metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE OR REPLACE FUNCTION projectpulse045_block_microsoft_integration_audit_mutation()
RETURNS trigger LANGUAGE plpgsql AS $projectpulse045_immutable_audit$
BEGIN
    RAISE EXCEPTION 'Microsoft Integration audit evidence is immutable.';
END;
$projectpulse045_immutable_audit$;

DROP TRIGGER IF EXISTS trg_projectpulse045_microsoft_integration_audit_immutable
ON microsoft_integration_audit_events;
CREATE TRIGGER trg_projectpulse045_microsoft_integration_audit_immutable
BEFORE UPDATE OR DELETE ON microsoft_integration_audit_events
FOR EACH ROW EXECUTE FUNCTION projectpulse045_block_microsoft_integration_audit_mutation();

CREATE TABLE IF NOT EXISTS microsoft_integration_permission_aliases (
    legacy_module_code TEXT NOT NULL,
    legacy_permission_code TEXT NOT NULL,
    active_module_code TEXT NOT NULL,
    active_route_scope TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (legacy_module_code, legacy_permission_code)
);

INSERT INTO microsoft_integration_permission_aliases (
    legacy_module_code,
    legacy_permission_code,
    active_module_code,
    active_route_scope
)
VALUES
    ('067', 'VIEW_GLOBAL_MAIL_CONFIGURATION', '065', 'entra-secret-administration'),
    ('067', 'MANAGE_GLOBAL_MAIL_CONFIGURATION', '065', 'entra-secret-administration'),
    ('067', 'VIEW_GLOBAL_MAIL', '065', 'entra-secret-administration'),
    ('067', 'MANAGE_GLOBAL_MAIL', '065', 'entra-secret-administration')
ON CONFLICT (legacy_module_code, legacy_permission_code)
DO UPDATE SET
    active_module_code = EXCLUDED.active_module_code,
    active_route_scope = EXCLUDED.active_route_scope;

DO $projectpulse045_catalog$
BEGIN
    IF to_regclass('public.scoped_role_policy_modules') IS NOT NULL THEN
        UPDATE scoped_role_policy_modules
        SET module_name = 'Microsoft Integration',
            route_scope = 'entra-secret-administration',
            current_state = 'Installed consolidated Microsoft integration',
            permission_notes = 'Owns Microsoft tenants, Entra applications and secrets, identity integration, directory synchronization, Microsoft 365 mail, sender configuration, connectivity tests, readiness, and sync status.',
            is_active = TRUE
        WHERE module_code = '065';

        UPDATE scoped_role_policy_modules
        SET current_state = 'Retired compatibility route mapped to Module 065',
            permission_notes = 'Historical Module 067 grants and configuration are preserved and mapped to Module 065 Microsoft Integration.',
            is_active = FALSE
        WHERE module_code = '067';
    END IF;
END;
$projectpulse045_catalog$;

DO $projectpulse045_registration$
BEGIN
    IF to_regclass('public.schema_migrations') IS NOT NULL THEN
        INSERT INTO schema_migrations (migration_id, description, applied_at)
        VALUES (
            '045_microsoft_integration_consolidation',
            'Consolidate Module 067 into Module 065 Microsoft Integration and add encrypted client-secret storage, immutable audit metadata, legacy permission aliases, and non-destructive route retirement',
            NOW()
        )
        ON CONFLICT (migration_id) DO UPDATE
        SET description = EXCLUDED.description,
            applied_at = EXCLUDED.applied_at;
    END IF;
END;
$projectpulse045_registration$;

COMMIT;
