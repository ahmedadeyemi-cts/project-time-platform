BEGIN;

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

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'ptp_app') THEN
        GRANT SELECT, INSERT, UPDATE ON projectpulse_module_availability TO ptp_app;
        GRANT SELECT, INSERT ON projectpulse_module_availability_audit TO ptp_app;
    END IF;
END
$$;

COMMIT;
