-- Project Pulse
-- Migration: 009_project_task_assignment_foundation_seed.sql
-- Purpose: Seed project/task assignment foundation so Open Tasks can be validated in the engineer timesheet.

BEGIN;

-- Ensure the development engineer exists with the current US Signal email.
INSERT INTO app_users (email, display_name, job_title, department, is_active)
VALUES ('ahmed.adeyemi@ussignal.com', 'Ahmed Adeyemi', 'Development Engineer', 'Professional Services', TRUE)
ON CONFLICT (email) DO UPDATE
SET display_name = EXCLUDED.display_name,
    job_title = EXCLUDED.job_title,
    department = EXCLUDED.department,
    updated_at = NOW();

-- Ensure the development PM exists.
INSERT INTO app_users (email, display_name, job_title, department, is_active)
VALUES ('matthew.lenoble@ussignal.com', 'Matthew LeNoble', 'Project Manager', 'Professional Services', TRUE)
ON CONFLICT (email) DO UPDATE
SET display_name = EXCLUDED.display_name,
    job_title = EXCLUDED.job_title,
    department = EXCLUDED.department,
    updated_at = NOW();

-- Seed a client aligned with the PSA charter validation flow.
INSERT INTO clients (client_name, client_code, is_active)
VALUES ('US Signal Internal', 'USS', TRUE)
ON CONFLICT (client_name) DO UPDATE
SET client_code = EXCLUDED.client_code,
    is_active = TRUE,
    updated_at = NOW();

-- Seed the PSA implementation project.
INSERT INTO projects (
    client_id,
    project_code,
    project_name,
    project_description,
    project_manager_user_id,
    status,
    start_date,
    end_date,
    billable
)
SELECT
    c.client_id,
    'USS-PSA-2026',
    'US Signal Professional Services Automation Platform',
    'Internal PSA platform build for project intake, scheduling, time, expense, approvals, invoicing, and reporting.',
    pm.user_id,
    'active',
    DATE '2026-07-06',
    DATE '2026-12-31',
    TRUE
FROM clients c
CROSS JOIN app_users pm
WHERE c.client_code = 'USS'
  AND pm.email = 'matthew.lenoble@ussignal.com'
ON CONFLICT (project_code) DO UPDATE
SET project_name = EXCLUDED.project_name,
    project_description = EXCLUDED.project_description,
    project_manager_user_id = EXCLUDED.project_manager_user_id,
    status = 'active',
    start_date = EXCLUDED.start_date,
    end_date = EXCLUDED.end_date,
    billable = TRUE,
    updated_at = NOW();

-- Seed representative project tasks from the charter phases.
INSERT INTO project_tasks (project_id, task_code, task_name, task_description, billable, is_active)
SELECT p.project_id, task_code, task_name, task_description, TRUE, TRUE
FROM projects p
CROSS JOIN (
    VALUES
        ('FOUNDATION', 'Foundation & Infrastructure', 'Azure, PostgreSQL, authentication, deployment, and base application shell.'),
        ('INTAKE', 'Project Intake & Templates', 'Client/project intake, template engine, kickoff checklist, and contract document tracking.'),
        ('PROJECT-MGMT', 'Project Management Module', 'Phases, milestones, tasks, dependencies, Gantt, risks, issues, RAG, and change orders.'),
        ('RESOURCE', 'Resource Scheduling', 'Resource assignment, capacity view, and Outlook calendar sync.'),
        ('TIME-EXPENSE', 'Time & Expense Management', 'Time tracking, expense entry, Emburse import, approvals, and receipt storage.'),
        ('INVOICE', 'Invoicing & Reporting', 'Invoice generation, financial dashboards, utilization reporting, and PM dashboard.'),
        ('UAT-GOLIVE', 'UAT, Training & Go-Live', 'UAT, training, onboarding, cutover, and hypercare readiness.')
) AS seed(task_code, task_name, task_description)
WHERE p.project_code = 'USS-PSA-2026'
ON CONFLICT (project_id, task_code) DO UPDATE
SET task_name = EXCLUDED.task_name,
    task_description = EXCLUDED.task_description,
    billable = TRUE,
    is_active = TRUE,
    updated_at = NOW();

-- Assign the development engineer to each seeded task so the Open Tasks dropdown has data.
INSERT INTO project_assignments (
    project_id,
    task_id,
    user_id,
    assigned_by_user_id,
    effective_start_date,
    effective_end_date,
    allocation_percent
)
SELECT
    p.project_id,
    pt.task_id,
    engineer.user_id,
    pm.user_id,
    DATE '2026-07-06',
    DATE '2026-12-31',
    50.00
FROM projects p
INNER JOIN project_tasks pt ON pt.project_id = p.project_id
CROSS JOIN app_users engineer
CROSS JOIN app_users pm
WHERE p.project_code = 'USS-PSA-2026'
  AND engineer.email = 'ahmed.adeyemi@ussignal.com'
  AND pm.email = 'matthew.lenoble@ussignal.com'
  AND NOT EXISTS (
      SELECT 1
      FROM project_assignments existing
      WHERE existing.project_id = p.project_id
        AND existing.task_id = pt.task_id
        AND existing.user_id = engineer.user_id
        AND existing.effective_start_date = DATE '2026-07-06'
  );

INSERT INTO schema_migrations (migration_id, description)
VALUES ('009_project_task_assignment_foundation_seed', 'Seed PSA project, phase tasks, and project assignments for Open Tasks validation')
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
