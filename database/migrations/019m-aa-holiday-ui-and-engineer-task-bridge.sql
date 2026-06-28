BEGIN;

-- Keep holiday management limited to Project Team Coordinator and Administrator.
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

-- Assignment hours are needed for assigned / used / remaining hour tracking.
ALTER TABLE project_assignments
ADD COLUMN IF NOT EXISTS assigned_hours numeric(12,2) NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS idx_project_assignments_user_project_task
ON project_assignments(user_id, project_id, task_id);

CREATE INDEX IF NOT EXISTS idx_time_entries_user_project_task
ON time_entries(user_id, project_id, task_id);

-- Bridge existing resource-request engineer assignments into project task assignments.
-- This makes assigned project tasks appear on the engineer timesheet.
WITH active_project_tasks AS (
    SELECT
        pt.project_id,
        pt.task_id,
        COUNT(*) OVER (PARTITION BY pt.project_id)::numeric AS task_count
    FROM project_tasks pt
    WHERE pt.is_active = TRUE
),
resource_assignments AS (
    SELECT
        err.project_id,
        erra.user_id,
        erra.assigned_by_user_id,
        COALESCE(err.target_start_date, CURRENT_DATE) AS effective_start_date,
        erra.allocated_hours,
        apt.task_id,
        apt.task_count
    FROM engineering_resource_requests err
    JOIN engineering_resource_request_assignments erra
        ON erra.engineering_resource_request_id = err.engineering_resource_request_id
    JOIN active_project_tasks apt
        ON apt.project_id = err.project_id
    WHERE err.project_id IS NOT NULL
)
INSERT INTO project_assignments (
    project_id,
    task_id,
    user_id,
    assigned_by_user_id,
    effective_start_date,
    allocation_percent,
    assigned_hours
)
SELECT
    ra.project_id,
    ra.task_id,
    ra.user_id,
    ra.assigned_by_user_id,
    ra.effective_start_date,
    NULL,
    ROUND(ra.allocated_hours / NULLIF(ra.task_count, 0), 2)
FROM resource_assignments ra
WHERE NOT EXISTS (
    SELECT 1
    FROM project_assignments existing
    WHERE existing.project_id = ra.project_id
      AND existing.task_id = ra.task_id
      AND existing.user_id = ra.user_id
);

-- Backfill any existing zero-hour assignments from resource request allocation.
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

GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE project_assignments TO "ptp_app";
GRANT SELECT ON TABLE projects, project_tasks, clients, time_entries TO "ptp_app";

COMMIT;
