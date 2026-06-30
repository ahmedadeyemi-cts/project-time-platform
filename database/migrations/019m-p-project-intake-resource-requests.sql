-- 019M-P Project Intake + Engineering Resource Request Workflow
-- Demo/staging foundation. Adds a dedicated request table for engineering staffing requests.

CREATE TABLE IF NOT EXISTS engineering_resource_requests (
    engineering_resource_request_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    request_number character varying(80) NOT NULL UNIQUE,
    project_intake_request_id uuid NULL REFERENCES project_intake_requests(project_intake_request_id),
    project_id uuid NULL REFERENCES projects(project_id),
    requested_by_user_id uuid NULL REFERENCES app_users(user_id),
    assigned_pm_user_id uuid NULL REFERENCES app_users(user_id),
    requested_function character varying(160) NOT NULL,
    skill_requirements text NULL,
    requested_hours numeric NOT NULL DEFAULT 0,
    target_start_date date NULL,
    target_end_date date NULL,
    priority character varying(40) NOT NULL DEFAULT 'normal',
    request_status character varying(60) NOT NULL DEFAULT 'requested',
    fulfilled_by_user_id uuid NULL REFERENCES app_users(user_id),
    assignment_notes text NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_engineering_resource_requests_status
ON engineering_resource_requests(request_status, priority);

CREATE INDEX IF NOT EXISTS idx_engineering_resource_requests_project
ON engineering_resource_requests(project_id);

CREATE INDEX IF NOT EXISTS idx_engineering_resource_requests_intake
ON engineering_resource_requests(project_intake_request_id);

CREATE INDEX IF NOT EXISTS idx_engineering_resource_requests_pm
ON engineering_resource_requests(assigned_pm_user_id);

INSERT INTO app_permissions (permission_code, permission_name, module_code, permission_description)
VALUES
  ('VIEW_ENGINEERING_RESOURCE_REQUESTS', 'View engineering resource requests', 'resources', 'View engineering resource request workflow and assignment readiness.'),
  ('MANAGE_ENGINEERING_RESOURCE_REQUESTS', 'Manage engineering resource requests', 'resources', 'Create, update, assign, and manage engineering resource requests.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code IN (
    'VIEW_ENGINEERING_RESOURCE_REQUESTS',
    'MANAGE_ENGINEERING_RESOURCE_REQUESTS'
)
WHERE r.role_code IN ('ADMINISTRATOR', 'MANAGER', 'PROJECT_MANAGEMENT', 'PROJECT_TEAM_COORDINATOR')
  AND NOT EXISTS (
      SELECT 1
      FROM app_role_permissions existing
      WHERE existing.app_role_id = r.app_role_id
        AND existing.app_permission_id = p.app_permission_id
  );

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code IN ('VIEW_ENGINEERING_RESOURCE_REQUESTS')
WHERE r.role_code IN ('EXECUTIVE')
  AND NOT EXISTS (
      SELECT 1
      FROM app_role_permissions existing
      WHERE existing.app_role_id = r.app_role_id
        AND existing.app_permission_id = p.app_permission_id
  );
