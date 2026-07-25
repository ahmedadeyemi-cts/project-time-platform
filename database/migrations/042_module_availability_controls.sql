-- ProjectPulse governed module availability foundation.
-- Additive after migration 041. No module is disabled and no module data is changed.
BEGIN;

DO $module_availability_042_prerequisite$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM schema_migrations
        WHERE migration_id = '041_module_001_timesheet_timer_and_task_association'
    ) THEN
        RAISE EXCEPTION 'Migration 042 requires 041_module_001_timesheet_timer_and_task_association first.';
    END IF;
END;
$module_availability_042_prerequisite$;

CREATE TABLE IF NOT EXISTS projectpulse_module_availability (
    module_number text PRIMARY KEY,
    route text NOT NULL,
    display_name text NOT NULL,
    is_enabled boolean NOT NULL DEFAULT TRUE,
    revision_number integer NOT NULL DEFAULT 1 CHECK (revision_number >= 1),
    reason text NULL CHECK (reason IS NULL OR length(reason) <= 1000),
    updated_by uuid NOT NULL REFERENCES app_users(user_id),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT projectpulse_module_availability_number_format
        CHECK (module_number ~ '^[0-9]{3}[A-Z]?$')
);

CREATE TABLE IF NOT EXISTS projectpulse_module_availability_audit (
    audit_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    module_number text NOT NULL,
    route text NOT NULL,
    display_name text NOT NULL,
    previous_enabled boolean NOT NULL,
    new_enabled boolean NOT NULL,
    previous_revision integer NOT NULL CHECK (previous_revision >= 0),
    new_revision integer NOT NULL CHECK (new_revision > previous_revision),
    reason text NULL CHECK (reason IS NULL OR length(reason) <= 1000),
    changed_by uuid NOT NULL REFERENCES app_users(user_id),
    changed_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_projectpulse_module_availability_audit_module_changed
    ON projectpulse_module_availability_audit (module_number, changed_at DESC);

CREATE INDEX IF NOT EXISTS ix_projectpulse_module_availability_audit_changed
    ON projectpulse_module_availability_audit (changed_at DESC);

COMMENT ON TABLE projectpulse_module_availability IS
    'Persistent module enable/disable state. Missing rows are treated by the application as enabled.';

COMMENT ON TABLE projectpulse_module_availability_audit IS
    'Immutable audit history for Super Administrator module availability changes.';

DO $module_availability_042_runtime_grants$
DECLARE
    role_name text;
BEGIN
    FOREACH role_name IN ARRAY ARRAY['ptp_app', 'projectpulse_app']
    LOOP
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = role_name) THEN
            EXECUTE format('GRANT USAGE ON SCHEMA public TO %I', role_name);
            EXECUTE format(
                'GRANT SELECT, INSERT, UPDATE ON TABLE projectpulse_module_availability TO %I',
                role_name
            );
            EXECUTE format(
                'GRANT SELECT, INSERT ON TABLE projectpulse_module_availability_audit TO %I',
                role_name
            );
        END IF;
    END LOOP;
END;
$module_availability_042_runtime_grants$;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '042_module_availability_controls',
    'Add persistent and audited module availability controls with default-enabled behavior',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
