BEGIN;

-- Engineers, PMs, PM leads, engineering leads, managers, and executives can view holidays,
-- but only Administrator and Project Team Coordinator can manage/upload/change holidays.
DELETE FROM app_role_permissions rp
USING app_roles r, app_permissions p
WHERE rp.app_role_id = r.app_role_id
  AND rp.app_permission_id = p.app_permission_id
  AND r.role_code IN (
      'ENGINEER',
      'PROJECT_MANAGEMENT',
      'ENGINEERING_TEAM_LEAD',
      'PROJECT_MANAGEMENT_TEAM_LEAD',
      'MANAGER',
      'EXECUTIVE'
  )
  AND p.permission_code = 'MANAGE_HOLIDAYS';

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code = 'VIEW_HOLIDAYS'
WHERE r.role_code IN (
    'ENGINEER',
    'PROJECT_MANAGEMENT',
    'ENGINEERING_TEAM_LEAD',
    'PROJECT_MANAGEMENT_TEAM_LEAD',
    'MANAGER',
    'PROJECT_TEAM_COORDINATOR',
    'EXECUTIVE',
    'ADMINISTRATOR'
)
AND NOT EXISTS (
    SELECT 1
    FROM app_role_permissions existing
    WHERE existing.app_role_id = r.app_role_id
      AND existing.app_permission_id = p.app_permission_id
);

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code = 'MANAGE_HOLIDAYS'
WHERE r.role_code IN ('PROJECT_TEAM_COORDINATOR', 'ADMINISTRATOR')
AND NOT EXISTS (
    SELECT 1
    FROM app_role_permissions existing
    WHERE existing.app_role_id = r.app_role_id
      AND existing.app_permission_id = p.app_permission_id
);

-- Project Info / Project Allocation page should not be visible to Engineers or Project Managers.
DELETE FROM app_role_permissions rp
USING app_roles r, app_permissions p
WHERE rp.app_role_id = r.app_role_id
  AND rp.app_permission_id = p.app_permission_id
  AND r.role_code IN ('ENGINEER', 'PROJECT_MANAGEMENT')
  AND p.permission_code IN (
      'VIEW_PROJECT_ALLOCATION_INFO',
      'MANAGE_PROJECT_ALLOCATION_INFO',
      'PURGE_PROJECT_DOCUMENTS'
  );

-- Workspace allocation-hour foundation.
ALTER TABLE project_assignments
ADD COLUMN IF NOT EXISTS assigned_hours numeric(12,2) NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS idx_project_assignments_user_project_task
ON project_assignments(user_id, project_id, task_id);

CREATE INDEX IF NOT EXISTS idx_time_entries_user_project_task
ON time_entries(user_id, project_id, task_id);

-- Backfill assigned hours from existing resource request allocations.
-- If one engineer has multiple tasks on the same project, distribute the resource request hours across those tasks.
WITH assignment_counts AS (
    SELECT
        project_id,
        user_id,
        COUNT(*)::numeric AS assignment_count
    FROM project_assignments
    GROUP BY project_id, user_id
),
resource_hours AS (
    SELECT
        err.project_id,
        erra.user_id,
        SUM(erra.allocated_hours)::numeric AS allocated_hours
    FROM engineering_resource_requests err
    JOIN engineering_resource_request_assignments erra
        ON erra.engineering_resource_request_id = err.engineering_resource_request_id
    WHERE err.project_id IS NOT NULL
    GROUP BY err.project_id, erra.user_id
)
UPDATE project_assignments pa
SET assigned_hours = ROUND(resource_hours.allocated_hours / NULLIF(assignment_counts.assignment_count, 0), 2)
FROM resource_hours
JOIN assignment_counts
    ON assignment_counts.project_id = resource_hours.project_id
   AND assignment_counts.user_id = resource_hours.user_id
WHERE pa.project_id = resource_hours.project_id
  AND pa.user_id = resource_hours.user_id
  AND pa.assigned_hours = 0;

COMMIT;
