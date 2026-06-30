-- 019M-AP Work Task Builder / Task Classification Foundation

INSERT INTO app_permissions (permission_code, permission_name, module_code, permission_description)
VALUES
    ('VIEW_WORK_TASK_BUILDER', 'View Work Task Builder', 'WORK_TASKS', 'View work task categories, task classifications, templates, and project task readiness.'),
    ('MANAGE_WORK_TASK_BUILDER', 'Manage Work Task Builder', 'WORK_TASKS', 'Create and manage global work task templates and classifications.'),
    ('ASSIGN_WORK_TASKS', 'Assign Work Tasks', 'WORK_TASKS', 'Assign project work tasks to engineers within allowed project scope.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
    ON p.permission_code IN ('VIEW_WORK_TASK_BUILDER', 'MANAGE_WORK_TASK_BUILDER', 'ASSIGN_WORK_TASKS')
WHERE r.role_code IN ('ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR')
ON CONFLICT DO NOTHING;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
    ON p.permission_code IN ('VIEW_WORK_TASK_BUILDER', 'ASSIGN_WORK_TASKS')
WHERE r.role_code IN ('PROJECT_MANAGEMENT', 'PROJECT_MANAGER')
ON CONFLICT DO NOTHING;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
    ON p.permission_code = 'VIEW_WORK_TASK_BUILDER'
WHERE r.role_code IN ('MANAGER', 'ENGINEERING_TEAM_LEAD', 'PROJECT_MANAGEMENT_TEAM_LEAD')
ON CONFLICT DO NOTHING;

CREATE TABLE IF NOT EXISTS work_task_templates (
    work_task_template_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    template_code VARCHAR(80) NOT NULL UNIQUE,
    template_name VARCHAR(200) NOT NULL,
    template_description TEXT,
    task_category VARCHAR(40) NOT NULL DEFAULT 'project_task',
    billing_classification VARCHAR(40) NOT NULL DEFAULT 'billable',
    utilization_classification VARCHAR(80) NOT NULL DEFAULT 'billable_utilization',
    utilization_bucket VARCHAR(80) NOT NULL DEFAULT 'billable',
    default_billable BOOLEAN NOT NULL DEFAULT TRUE,
    default_requires_approval BOOLEAN NOT NULL DEFAULT TRUE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    display_order INTEGER NOT NULL DEFAULT 100,
    created_by_user_id UUID NULL REFERENCES app_users(user_id),
    updated_by_user_id UUID NULL REFERENCES app_users(user_id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_work_task_templates_task_category
        CHECK (task_category IN ('open_task', 'project_task', 'service_request_task', 'non_project_task')),
    CONSTRAINT ck_work_task_templates_billing_classification
        CHECK (billing_classification IN ('billable', 'non_billable')),
    CONSTRAINT ck_work_task_templates_utilization_classification
        CHECK (utilization_classification IN ('billable_utilization', 'non_billable_utilization', 'non_billable_non_utilization'))
);

ALTER TABLE project_tasks
    ADD COLUMN IF NOT EXISTS work_task_category VARCHAR(40) NOT NULL DEFAULT 'project_task',
    ADD COLUMN IF NOT EXISTS billing_classification VARCHAR(40) NOT NULL DEFAULT 'billable',
    ADD COLUMN IF NOT EXISTS utilization_classification VARCHAR(80) NOT NULL DEFAULT 'billable_utilization',
    ADD COLUMN IF NOT EXISTS service_request_number TEXT NULL,
    ADD COLUMN IF NOT EXISTS work_task_notes TEXT NULL,
    ADD COLUMN IF NOT EXISTS work_task_template_id UUID NULL REFERENCES work_task_templates(work_task_template_id);

ALTER TABLE project_assignments
    ADD COLUMN IF NOT EXISTS assignment_source VARCHAR(80) NOT NULL DEFAULT 'project_assignment',
    ADD COLUMN IF NOT EXISTS assignment_notes TEXT NULL,
    ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW();

UPDATE project_tasks
SET billing_classification = CASE WHEN billable THEN 'billable' ELSE 'non_billable' END
WHERE billing_classification IS NULL
   OR billing_classification NOT IN ('billable', 'non_billable');

UPDATE project_tasks
SET utilization_classification = CASE
        WHEN billable THEN 'billable_utilization'
        WHEN utilization_bucket IN ('non_billable', 'presales_training') THEN 'non_billable_utilization'
        ELSE 'non_billable_non_utilization'
    END
WHERE utilization_classification IS NULL
   OR utilization_classification NOT IN ('billable_utilization', 'non_billable_utilization', 'non_billable_non_utilization');

UPDATE project_tasks
SET work_task_category = 'project_task'
WHERE work_task_category IS NULL
   OR work_task_category NOT IN ('open_task', 'project_task', 'service_request_task', 'non_project_task');

INSERT INTO work_task_templates (
    template_code,
    template_name,
    template_description,
    task_category,
    billing_classification,
    utilization_classification,
    utilization_bucket,
    default_billable,
    default_requires_approval,
    display_order
)
VALUES
    ('PROJECT_BILLABLE_DELIVERY', 'Project Task - Billable Delivery', 'Standard project delivery work tied to a client project and eligible for billable utilization.', 'project_task', 'billable', 'billable_utilization', 'billable', TRUE, TRUE, 10),
    ('SERVICE_REQUEST_BILLABLE', 'Service Request Task - Billable Support', 'Billable service request or support work associated with a customer request.', 'service_request_task', 'billable', 'billable_utilization', 'billable', TRUE, TRUE, 20),
    ('OPEN_TASK_NON_BILLABLE_UTILIZATION', 'Open Task - Non-Billable Utilization', 'Internal open work that still counts toward productive non-billable utilization.', 'open_task', 'non_billable', 'non_billable_utilization', 'non_billable', FALSE, TRUE, 30),
    ('NON_PROJECT_INTERNAL', 'Non-Project Task - Internal Operations', 'Internal non-project work that is non-billable and does not count toward productive utilization.', 'non_project_task', 'non_billable', 'non_billable_non_utilization', 'excluded', FALSE, TRUE, 40)
ON CONFLICT (template_code) DO UPDATE
SET template_name = EXCLUDED.template_name,
    template_description = EXCLUDED.template_description,
    task_category = EXCLUDED.task_category,
    billing_classification = EXCLUDED.billing_classification,
    utilization_classification = EXCLUDED.utilization_classification,
    utilization_bucket = EXCLUDED.utilization_bucket,
    default_billable = EXCLUDED.default_billable,
    default_requires_approval = EXCLUDED.default_requires_approval,
    display_order = EXCLUDED.display_order,
    updated_at = NOW();

CREATE INDEX IF NOT EXISTS idx_work_task_templates_category
    ON work_task_templates(task_category, is_active, display_order);

CREATE INDEX IF NOT EXISTS idx_project_tasks_work_task_category
    ON project_tasks(work_task_category, billing_classification, utilization_classification);

CREATE INDEX IF NOT EXISTS idx_project_assignments_user_task_dates
    ON project_assignments(user_id, task_id, effective_start_date, effective_end_date);

DO $$
DECLARE
    role_name text;
BEGIN
    FOREACH role_name IN ARRAY ARRAY['ptp_app', 'projectpulse_app']
    LOOP
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = role_name) THEN
            EXECUTE format('GRANT USAGE ON SCHEMA public TO %I', role_name);
            EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE work_task_templates TO %I', role_name);
            EXECUTE format('GRANT SELECT, UPDATE ON TABLE project_tasks TO %I', role_name);
            EXECUTE format('GRANT SELECT, INSERT, UPDATE ON TABLE project_assignments TO %I', role_name);
            EXECUTE format('GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO %I', role_name);
        END IF;
    END LOOP;
END $$;
