-- 019M-AL Approval / Export / Audit Workflow Foundation

INSERT INTO app_permissions (
    permission_code,
    permission_name,
    module_code,
    permission_description
)
VALUES
    ('VIEW_APPROVAL_WORKFLOW', 'View Approval Workflow', 'APPROVAL_WORKFLOW', 'View approval, project validation, accounting reconciliation, export, and audit workflow summaries.'),
    ('PROJECT_TIME_APPROVAL', 'Project Time Approval', 'APPROVAL_WORKFLOW', 'Validate manager-approved project time before accounting review.'),
    ('VIEW_ACCOUNT_RECONCILIATION', 'View Account Reconciliation', 'APPROVAL_WORKFLOW', 'View accounting reconciliation workflow state.'),
    ('MANAGE_ACCOUNT_RECONCILIATION', 'Manage Account Reconciliation', 'APPROVAL_WORKFLOW', 'Mark approved time as accounting-ready, reconciled, or locked.'),
    ('EXPORT_TIME_EXCEL', 'Export Time Excel', 'APPROVAL_WORKFLOW', 'Prepare Excel-ready time export records.'),
    ('EXPORT_TIME_PDF', 'Export Time PDF', 'APPROVAL_WORKFLOW', 'Prepare PDF-ready time export records.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code IN (
    'VIEW_APPROVAL_WORKFLOW',
    'PROJECT_TIME_APPROVAL'
)
WHERE r.role_code IN ('ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR', 'PROJECT_MANAGEMENT', 'PROJECT_MANAGER')
ON CONFLICT DO NOTHING;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code IN (
    'VIEW_APPROVAL_WORKFLOW',
    'VIEW_ACCOUNT_RECONCILIATION',
    'MANAGE_ACCOUNT_RECONCILIATION',
    'EXPORT_TIME_EXCEL',
    'EXPORT_TIME_PDF'
)
WHERE r.role_code IN ('ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR')
ON CONFLICT DO NOTHING;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code = 'VIEW_APPROVAL_WORKFLOW'
WHERE r.role_code IN ('EXECUTIVE')
ON CONFLICT DO NOTHING;

ALTER TABLE timesheet_day_statuses
    ADD COLUMN IF NOT EXISTS pm_approved_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS pm_approved_by_user_id uuid REFERENCES app_users(user_id),
    ADD COLUMN IF NOT EXISTS pm_decision_comment text,
    ADD COLUMN IF NOT EXISTS accounting_ready_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS accounting_ready_by_user_id uuid REFERENCES app_users(user_id),
    ADD COLUMN IF NOT EXISTS accounting_comment text,
    ADD COLUMN IF NOT EXISTS reconciled_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS reconciled_by_user_id uuid REFERENCES app_users(user_id),
    ADD COLUMN IF NOT EXISTS reconciliation_comment text,
    ADD COLUMN IF NOT EXISTS locked_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS locked_by_user_id uuid REFERENCES app_users(user_id),
    ADD COLUMN IF NOT EXISTS lock_comment text;

CREATE TABLE IF NOT EXISTS time_workflow_exports (
    time_workflow_export_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    export_format character varying(20) NOT NULL,
    week_start date,
    week_end date,
    export_status character varying(40) NOT NULL DEFAULT 'prepared',
    requested_by_user_id uuid REFERENCES app_users(user_id),
    requested_by_email character varying(255),
    item_count integer NOT NULL DEFAULT 0,
    total_hours numeric(12,2) NOT NULL DEFAULT 0,
    file_name character varying(255),
    notes text,
    created_at timestamp with time zone NOT NULL DEFAULT NOW(),
    updated_at timestamp with time zone NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_time_workflow_exports_created_at
    ON time_workflow_exports(created_at DESC);

CREATE INDEX IF NOT EXISTS ix_timesheet_day_statuses_workflow_status
    ON timesheet_day_statuses(status, work_date);

-- Ensure API runtime roles can use the export foundation table.
DO $$
DECLARE
    role_name text;
BEGIN
    FOREACH role_name IN ARRAY ARRAY['ptp_app', 'projectpulse_app']
    LOOP
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = role_name) THEN
            EXECUTE format('GRANT USAGE ON SCHEMA public TO %I', role_name);
            EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE time_workflow_exports TO %I', role_name);
            EXECUTE format('GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO %I', role_name);
        END IF;
    END LOOP;
END $$;

