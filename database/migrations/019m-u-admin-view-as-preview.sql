BEGIN;

CREATE TABLE IF NOT EXISTS projectpulse_admin_view_as_audit (
    admin_view_as_audit_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    administrator_user_id uuid NOT NULL REFERENCES app_users(user_id),
    viewed_as_user_id uuid NOT NULL REFERENCES app_users(user_id),
    viewed_route text NULL,
    preview_mode varchar(60) NOT NULL DEFAULT 'read_only',
    action_taken varchar(120) NOT NULL DEFAULT 'view_as_started',
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_projectpulse_admin_view_as_audit_admin
ON projectpulse_admin_view_as_audit(administrator_user_id, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_projectpulse_admin_view_as_audit_target
ON projectpulse_admin_view_as_audit(viewed_as_user_id, created_at DESC);

GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE projectpulse_admin_view_as_audit TO "ptp_app";

COMMIT;
