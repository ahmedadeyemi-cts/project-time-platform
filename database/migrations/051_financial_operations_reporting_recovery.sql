-- ProjectPulse Group 5
-- Modules 030, 031, 039, 040, 041, and 042.
-- Durable report runs, financial-operations recovery queue, source-retry evidence,
-- and role permissions. Module 038 remains regression-only.

BEGIN;

CREATE TABLE IF NOT EXISTS financial_report_runs (
    financial_report_run_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    report_code VARCHAR(120) NOT NULL,
    report_name VARCHAR(220) NOT NULL,
    actual_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    effective_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    filters_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    result_status VARCHAR(40) NOT NULL CHECK (result_status IN (
        'complete', 'partial', 'no_data', 'source_unavailable', 'failed'
    )),
    row_count INTEGER NOT NULL DEFAULT 0 CHECK (row_count >= 0),
    source_states_json JSONB NOT NULL DEFAULT '[]'::JSONB,
    result_json JSONB NOT NULL DEFAULT '[]'::JSONB,
    diagnostic_code VARCHAR(120) NOT NULL DEFAULT '',
    diagnostic_message TEXT NOT NULL DEFAULT '',
    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ NULL,
    last_exported_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_financial_report_runs_actor
    ON financial_report_runs(effective_user_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_financial_report_runs_report
    ON financial_report_runs(report_code, created_at DESC);

CREATE TABLE IF NOT EXISTS financial_operations_work_items (
    financial_operations_work_item_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    deduplication_key VARCHAR(300) NOT NULL UNIQUE,
    project_id UUID NULL REFERENCES projects(project_id) ON DELETE SET NULL,
    module_code VARCHAR(20) NOT NULL,
    item_type VARCHAR(100) NOT NULL,
    source_key VARCHAR(120) NOT NULL DEFAULT '',
    priority VARCHAR(20) NOT NULL DEFAULT 'medium' CHECK (priority IN (
        'low', 'medium', 'high', 'critical'
    )),
    work_status VARCHAR(30) NOT NULL DEFAULT 'open' CHECK (work_status IN (
        'open', 'acknowledged', 'resolved', 'dismissed'
    )),
    title VARCHAR(320) NOT NULL,
    detail TEXT NOT NULL DEFAULT '',
    owner_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    retry_endpoint TEXT NOT NULL DEFAULT '',
    first_detected_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_detected_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    acknowledged_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    acknowledged_at TIMESTAMPTZ NULL,
    resolved_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    resolved_at TIMESTAMPTZ NULL,
    resolution_note TEXT NOT NULL DEFAULT '',
    metadata_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_financial_operations_work_items_queue
    ON financial_operations_work_items(work_status, priority, last_detected_at DESC);
CREATE INDEX IF NOT EXISTS ix_financial_operations_work_items_project
    ON financial_operations_work_items(project_id, work_status, last_detected_at DESC);

CREATE TABLE IF NOT EXISTS financial_operations_actions (
    financial_operations_action_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    financial_operations_work_item_id UUID NULL REFERENCES financial_operations_work_items(financial_operations_work_item_id) ON DELETE SET NULL,
    project_id UUID NULL REFERENCES projects(project_id) ON DELETE SET NULL,
    source_key VARCHAR(120) NOT NULL DEFAULT '',
    action_code VARCHAR(120) NOT NULL,
    action_status VARCHAR(40) NOT NULL CHECK (action_status IN (
        'requested', 'succeeded', 'partial', 'failed', 'suppressed'
    )),
    actor_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    diagnostic_code VARCHAR(120) NOT NULL DEFAULT '',
    diagnostic_message TEXT NOT NULL DEFAULT '',
    correlation_id VARCHAR(160) NOT NULL DEFAULT '',
    metadata_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_financial_operations_actions_item
    ON financial_operations_actions(financial_operations_work_item_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_financial_operations_actions_source
    ON financial_operations_actions(source_key, created_at DESC);

CREATE OR REPLACE FUNCTION projectpulse051_block_financial_action_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse051_financial_action_immutable$
BEGIN
    RAISE EXCEPTION 'Financial operations action evidence is immutable.';
END;
$projectpulse051_financial_action_immutable$;

DROP TRIGGER IF EXISTS trg_projectpulse051_financial_actions_immutable
    ON financial_operations_actions;
CREATE TRIGGER trg_projectpulse051_financial_actions_immutable
BEFORE UPDATE OR DELETE ON financial_operations_actions
FOR EACH ROW EXECUTE FUNCTION projectpulse051_block_financial_action_mutation();

INSERT INTO app_permissions (
    permission_code,
    permission_name,
    module_code,
    permission_description
)
VALUES
    ('VIEW_FINANCIAL_REPORT_CENTER', 'View Financial Report Center', '030', 'View the governed financial report catalog and role-scoped results.'),
    ('RUN_FINANCIAL_REPORTS', 'Run Financial Reports', '030', 'Preview and run role-scoped financial reports with source evidence.'),
    ('EXPORT_FINANCIAL_REPORTS', 'Export Financial Reports', '030', 'Export persisted role-scoped report results.'),
    ('VIEW_FINANCIAL_OPERATIONS_WORKBENCH', 'View Financial Operations Workbench', '031', 'View source failures, billing blockers, closeout blockers, reconciliation exceptions, and notification failures.'),
    ('MANAGE_FINANCIAL_OPERATIONS_RECOVERY', 'Manage Financial Operations Recovery', '031', 'Refresh, acknowledge, assign, and resolve financial operations work items.'),
    ('RETRY_FINANCIAL_SOURCES', 'Retry Financial Sources', '031', 'Run bounded source-specific retries and record immutable diagnostic evidence.'),
    ('VIEW_ACCOUNTING_RECONCILIATION_RECOVERY', 'View Accounting Reconciliation Recovery', '039', 'View billing-readiness and reconciliation source recovery details.'),
    ('VIEW_PROJECT_CLOSEOUT_RECOVERY', 'View Project Closeout Recovery', '040', 'View project closeout blockers, source status, and recovery evidence.'),
    ('VIEW_CLOSEOUT_NOTIFICATION_RECOVERY', 'View Closeout Notification Recovery', '041', 'View Group 4 closeout dispatch and Module 065 delivery recovery evidence.'),
    ('VIEW_BILLING_RECOVERY', 'View Billing Recovery', '042', 'View approved-time, current-expense, invoice-readiness, and billing-source recovery evidence.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_feature_catalog (
    feature_code,
    feature_name,
    module_code,
    route_anchor,
    required_permission_code,
    feature_description,
    display_order,
    is_active
)
VALUES
    ('FINANCIAL_REPORT_CENTER', 'Financial Report Center', '030', '#reporting', 'VIEW_FINANCIAL_REPORT_CENTER', 'Search, preview, run, export, and review history for actual role-scoped financial reports.', 300, TRUE),
    ('FINANCIAL_OPERATIONS_WORKBENCH', 'Financial Operations Workbench', '031', '#financial-operations-workbench', 'VIEW_FINANCIAL_OPERATIONS_WORKBENCH', 'One recovery queue for financial-source failures, billing, closeout, reconciliation, and notification exceptions.', 310, TRUE),
    ('BILLING_READINESS_RECOVERY', 'Billing Readiness Recovery', '039', '#billing-readiness', 'VIEW_ACCOUNTING_RECONCILIATION_RECOVERY', 'Source-specific billing-readiness and reconciliation recovery.' , 390, TRUE),
    ('PROJECT_CLOSEOUT_RECOVERY', 'Project Closeout Recovery', '040', '#project-closeout', 'VIEW_PROJECT_CLOSEOUT_RECOVERY', 'Source-specific closeout blockers and recovery evidence.', 400, TRUE),
    ('CLOSEOUT_NOTIFICATION_RECOVERY', 'Closeout Notification Recovery', '041', '#closeout-email', 'VIEW_CLOSEOUT_NOTIFICATION_RECOVERY', 'Group 4 dispatch and Module 065 delivery recovery evidence.', 410, TRUE),
    ('BILLING_RECOVERY', 'Billing Recovery', '042', '#invoice-billing-center', 'VIEW_BILLING_RECOVERY', 'Approved-time and current-expense billing recovery without duplicating Module 005.', 420, TRUE)
ON CONFLICT (feature_code) DO UPDATE
SET feature_name = EXCLUDED.feature_name,
    module_code = EXCLUDED.module_code,
    route_anchor = EXCLUDED.route_anchor,
    required_permission_code = EXCLUDED.required_permission_code,
    feature_description = EXCLUDED.feature_description,
    display_order = EXCLUDED.display_order,
    is_active = TRUE,
    updated_at = NOW();

-- Accounting, finance, PTC, and administrators receive full recovery authority.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
CROSS JOIN app_permissions permission
WHERE upper(role.role_code) IN (
    'ACCOUNTING', 'ACCOUNTING_BILLING', 'BILLING', 'FINANCE',
    'PROJECT_TEAM_COORDINATOR', 'SUPER_ADMINISTRATOR', 'ADMINISTRATOR'
)
  AND permission.permission_code IN (
    'VIEW_FINANCIAL_REPORT_CENTER',
    'RUN_FINANCIAL_REPORTS',
    'EXPORT_FINANCIAL_REPORTS',
    'VIEW_FINANCIAL_OPERATIONS_WORKBENCH',
    'MANAGE_FINANCIAL_OPERATIONS_RECOVERY',
    'RETRY_FINANCIAL_SOURCES',
    'VIEW_ACCOUNTING_RECONCILIATION_RECOVERY',
    'VIEW_PROJECT_CLOSEOUT_RECOVERY',
    'VIEW_CLOSEOUT_NOTIFICATION_RECOVERY',
    'VIEW_BILLING_RECOVERY'
  )
ON CONFLICT DO NOTHING;

-- Project Management can run scoped reports and review project closeout/billing recovery.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code IN (
    'VIEW_FINANCIAL_REPORT_CENTER',
    'RUN_FINANCIAL_REPORTS',
    'VIEW_FINANCIAL_OPERATIONS_WORKBENCH',
    'VIEW_PROJECT_CLOSEOUT_RECOVERY',
    'VIEW_CLOSEOUT_NOTIFICATION_RECOVERY',
    'VIEW_BILLING_RECOVERY'
)
WHERE upper(role.role_code) IN (
    'PROJECT_MANAGER', 'PROJECT_MANAGEMENT',
    'PROJECT_MANAGEMENT_LEAD', 'PROJECT_MANAGEMENT_TEAM_LEAD', 'PM_TEAM_LEAD'
)
ON CONFLICT DO NOTHING;

-- Executives receive read-only organization reporting and workbench visibility.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code IN (
    'VIEW_FINANCIAL_REPORT_CENTER',
    'VIEW_FINANCIAL_OPERATIONS_WORKBENCH',
    'VIEW_ACCOUNTING_RECONCILIATION_RECOVERY',
    'VIEW_PROJECT_CLOSEOUT_RECOVERY',
    'VIEW_CLOSEOUT_NOTIFICATION_RECOVERY',
    'VIEW_BILLING_RECOVERY'
)
WHERE upper(role.role_code) IN ('EXECUTIVE', 'EXECUTIVE_LEADERSHIP')
ON CONFLICT DO NOTHING;

-- Engineering, managers, sales, and Solution Architects can run reports within
-- the role-appropriate Group 3 project and financial visibility boundary.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code IN (
    'VIEW_FINANCIAL_REPORT_CENTER',
    'RUN_FINANCIAL_REPORTS'
)
WHERE upper(role.role_code) IN (
    'ENGINEERING', 'ENGINEER', 'ENGINEERING_LEAD', 'ENGINEERING_TEAM_LEAD',
    'MANAGER', 'SALES', 'INSIDE_SALES', 'ACCOUNT_EXECUTIVE',
    'SOLUTION_ARCHITECT', 'SALES_ENGINEERING'
)
ON CONFLICT DO NOTHING;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '051_financial_operations_reporting_recovery',
    'Group 5 report runs, Module 031 financial operations workbench, source retries, closeout recovery, and billing recovery',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
