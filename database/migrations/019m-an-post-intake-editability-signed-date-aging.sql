-- 019M-AN Post-Intake Editability + Signed Date Aging

INSERT INTO app_permissions (permission_code, permission_name, module_code, permission_description)
VALUES
    ('VIEW_PROJECT_INTAKE_AGING', 'View Project Intake Aging', 'PROJECT_INTAKE', 'View project signed date aging, movement status, and intake aging readiness.'),
    ('MANAGE_PROJECT_INTAKE_AGING', 'Manage Project Intake Aging', 'PROJECT_INTAKE', 'Edit post-intake information, signed date, and intake aging notes.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
    ON p.permission_code IN ('VIEW_PROJECT_INTAKE_AGING', 'MANAGE_PROJECT_INTAKE_AGING')
WHERE r.role_code IN ('ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR')
ON CONFLICT DO NOTHING;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
    ON p.permission_code = 'VIEW_PROJECT_INTAKE_AGING'
WHERE r.role_code IN ('PROJECT_MANAGER', 'PROJECT_MANAGEMENT', 'PM_TEAM_LEAD', 'PROJECT_MANAGEMENT_TEAM_LEAD')
ON CONFLICT DO NOTHING;

ALTER TABLE project_intake_requests
    ADD COLUMN IF NOT EXISTS project_signed_date date,
    ADD COLUMN IF NOT EXISTS signed_date_recorded_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS signed_date_recorded_by_user_id uuid REFERENCES app_users(user_id),
    ADD COLUMN IF NOT EXISTS triage_started_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS resource_request_started_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS pm_assignment_started_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS post_intake_edit_count integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS last_post_intake_edit_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS last_post_intake_edit_by_user_id uuid REFERENCES app_users(user_id),
    ADD COLUMN IF NOT EXISTS last_post_intake_edit_note text,
    ADD COLUMN IF NOT EXISTS aging_notification_stage character varying(40),
    ADD COLUMN IF NOT EXISTS aging_notification_last_evaluated_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS aging_notification_last_message text;

UPDATE project_intake_requests
SET project_signed_date = COALESCE(project_signed_date, source_received_at::date, created_at::date)
WHERE project_signed_date IS NULL
  AND intake_status IN ('triage', 'requested', 'resource_requested', 'assigned', 'active');

UPDATE project_intake_requests
SET triage_started_at = COALESCE(triage_started_at, updated_at, created_at)
WHERE triage_started_at IS NULL
  AND intake_status IN ('triage', 'requested', 'resource_requested', 'assigned', 'active');

UPDATE project_intake_requests
SET pm_assignment_started_at = COALESCE(pm_assignment_started_at, updated_at, created_at)
WHERE pm_assignment_started_at IS NULL
  AND assigned_pm_user_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS project_intake_change_history (
    project_intake_change_history_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    project_intake_request_id uuid NOT NULL REFERENCES project_intake_requests(project_intake_request_id) ON DELETE CASCADE,
    changed_by_user_id uuid REFERENCES app_users(user_id),
    change_type character varying(80) NOT NULL,
    change_summary text NOT NULL,
    previous_snapshot jsonb,
    new_snapshot jsonb,
    created_at timestamp with time zone NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS project_intake_aging_notification_events (
    project_intake_aging_notification_event_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    project_intake_request_id uuid NOT NULL REFERENCES project_intake_requests(project_intake_request_id) ON DELETE CASCADE,
    notification_stage character varying(40) NOT NULL,
    notification_status character varying(40) NOT NULL DEFAULT 'preview',
    recipient_summary text,
    message text,
    created_at timestamp with time zone NOT NULL DEFAULT NOW(),
    UNIQUE (project_intake_request_id, notification_stage)
);

CREATE INDEX IF NOT EXISTS ix_project_intake_requests_signed_date
    ON project_intake_requests(project_signed_date);

CREATE INDEX IF NOT EXISTS ix_project_intake_change_history_request_created
    ON project_intake_change_history(project_intake_request_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_project_intake_aging_events_request_stage
    ON project_intake_aging_notification_events(project_intake_request_id, notification_stage);


-- 019M-AN runtime grants
DO $$
DECLARE
    role_name text;
BEGIN
    FOREACH role_name IN ARRAY ARRAY['ptp_app', 'projectpulse_app']
    LOOP
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = role_name) THEN
            EXECUTE format('GRANT USAGE ON SCHEMA public TO %I', role_name);
            EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE project_intake_change_history TO %I', role_name);
            EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE project_intake_aging_notification_events TO %I', role_name);
            EXECUTE format('GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO %I', role_name);
        END IF;
    END LOOP;
END $$;
