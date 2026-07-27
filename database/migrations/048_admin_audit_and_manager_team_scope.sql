-- ProjectPulse Modules 008 and 009
-- Unified immutable administrative audit evidence and manager-to-team scope.
-- Additive only. Does not modify Module 010 or Module 065 configuration.
BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS projectpulse_system_audit_events (
    projectpulse_system_audit_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    event_time TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    category TEXT NOT NULL DEFAULT 'system',
    status TEXT NOT NULL DEFAULT 'info',
    event_type TEXT NOT NULL,
    actor_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    actor_email TEXT NOT NULL DEFAULT '',
    target_type TEXT NOT NULL DEFAULT '',
    target_id TEXT NOT NULL DEFAULT '',
    target_label TEXT NOT NULL DEFAULT '',
    source_module TEXT NOT NULL DEFAULT '',
    source_table TEXT NOT NULL DEFAULT '',
    source_record_id TEXT NOT NULL DEFAULT '',
    summary TEXT NOT NULL DEFAULT '',
    event_details JSONB NOT NULL DEFAULT '{}'::jsonb,
    ip_address TEXT NOT NULL DEFAULT '',
    correlation_id TEXT NOT NULL DEFAULT '',
    is_immutable BOOLEAN NOT NULL DEFAULT TRUE CHECK (is_immutable = TRUE),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_projectpulse_system_audit_event_time
ON projectpulse_system_audit_events(event_time DESC);

CREATE INDEX IF NOT EXISTS ix_projectpulse_system_audit_category_status
ON projectpulse_system_audit_events(category, status, event_time DESC);

CREATE INDEX IF NOT EXISTS ix_projectpulse_system_audit_actor
ON projectpulse_system_audit_events(actor_user_id, event_time DESC)
WHERE actor_user_id IS NOT NULL;

CREATE OR REPLACE FUNCTION projectpulse048_block_system_audit_mutation()
RETURNS trigger LANGUAGE plpgsql AS $projectpulse048_immutable_audit$
BEGIN
    RAISE EXCEPTION 'ProjectPulse system audit evidence is immutable.';
END;
$projectpulse048_immutable_audit$;

DROP TRIGGER IF EXISTS trg_projectpulse048_system_audit_immutable
ON projectpulse_system_audit_events;
CREATE TRIGGER trg_projectpulse048_system_audit_immutable
BEFORE UPDATE OR DELETE ON projectpulse_system_audit_events
FOR EACH ROW EXECUTE FUNCTION projectpulse048_block_system_audit_mutation();

CREATE TABLE IF NOT EXISTS user_admin_manager_team_assignments (
    user_admin_manager_team_assignment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    manager_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    manager_email TEXT NOT NULL,
    team_name TEXT NOT NULL CHECK (length(trim(team_name)) > 0),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    assigned_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    assignment_reason TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(manager_user_id, team_name)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_user_admin_one_active_manager_per_team
ON user_admin_manager_team_assignments(lower(team_name))
WHERE is_active = TRUE;

CREATE INDEX IF NOT EXISTS ix_user_admin_manager_team_manager
ON user_admin_manager_team_assignments(manager_user_id, is_active, team_name);

CREATE INDEX IF NOT EXISTS ix_user_admin_manager_team_team
ON user_admin_manager_team_assignments(lower(team_name), is_active);

DO $projectpulse048_runtime_grants$
DECLARE
    runtime_role TEXT;
BEGIN
    FOREACH runtime_role IN ARRAY ARRAY['ptp_app', 'projectpulse_app']
    LOOP
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = runtime_role) THEN
            EXECUTE format(
                'GRANT SELECT, INSERT ON TABLE projectpulse_system_audit_events TO %I',
                runtime_role
            );
            EXECUTE format(
                'GRANT SELECT, INSERT, UPDATE ON TABLE user_admin_manager_team_assignments TO %I',
                runtime_role
            );
        END IF;
    END LOOP;
END;
$projectpulse048_runtime_grants$;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '048_admin_audit_and_manager_team_scope',
    'Modules 008 and 009 unified immutable audit evidence plus manager-to-multiple-team scope and automatic manager-email reconciliation',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
