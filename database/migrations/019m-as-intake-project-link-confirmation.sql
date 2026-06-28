-- 019M-AS Intake Project Link Confirmation + Resource Assignment Handoff

INSERT INTO app_permissions (permission_code, permission_name, module_code, permission_description)
VALUES
    ('MANAGE_INTAKE_PROJECT_LINKS', 'Manage Intake Project Links', 'PROJECT_INTAKE', 'Confirm the project record that belongs to an intake request without enabling automatic conversion.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
    ON p.permission_code = 'MANAGE_INTAKE_PROJECT_LINKS'
WHERE r.role_code IN (
    'ADMINISTRATOR',
    'PROJECT_TEAM_COORDINATOR',
    'PROJECT_MANAGEMENT',
    'PROJECT_MANAGER',
    'PM_TEAM_LEAD',
    'PROJECT_MANAGEMENT_TEAM_LEAD'
)
ON CONFLICT DO NOTHING;

CREATE TABLE IF NOT EXISTS project_intake_project_links (
    project_intake_project_link_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    project_intake_request_id uuid NOT NULL REFERENCES project_intake_requests(project_intake_request_id) ON DELETE CASCADE,
    project_id uuid NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    link_status character varying(50) NOT NULL DEFAULT 'confirmed',
    link_source character varying(50) NOT NULL DEFAULT 'manual_confirmation',
    confirmation_note text NULL,
    confirmed_by_user_id uuid NULL REFERENCES app_users(user_id),
    confirmed_at timestamp with time zone NOT NULL DEFAULT now(),
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT chk_project_intake_project_link_status CHECK (link_status IN ('confirmed', 'superseded', 'removed'))
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_project_intake_project_links_pair
ON project_intake_project_links(project_intake_request_id, project_id);

CREATE INDEX IF NOT EXISTS idx_project_intake_project_links_intake_active
ON project_intake_project_links(project_intake_request_id, is_active, link_status);

CREATE INDEX IF NOT EXISTS idx_project_intake_project_links_project_active
ON project_intake_project_links(project_id, is_active, link_status);

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'ptp_app') THEN
        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE project_intake_project_links TO ptp_app;
    END IF;

    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'projectpulse_app') THEN
        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE project_intake_project_links TO projectpulse_app;
    END IF;
END $$;
