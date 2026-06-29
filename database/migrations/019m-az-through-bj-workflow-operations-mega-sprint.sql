-- 019M-AZ through 019M-BJ Workflow Operations Mega Sprint

INSERT INTO app_permissions (permission_code, permission_name, module_code, permission_description)
VALUES
    ('VIEW_AUDIT_HISTORY_EVENTS', 'View Audit History Events', 'APPROVAL_WORKFLOW', 'View audit history events for workflow, export, reconciliation, and administrative activity.'),
    ('VIEW_WORKFLOW_ACTION_CAPABILITIES', 'View Workflow Action Capabilities', 'APPROVAL_WORKFLOW', 'View allowed workflow actions, next-step readiness, and dry-run workflow planning.'),
    ('VIEW_MODULE_VISIBILITY_SMOKE', 'View Module Visibility Smoke', 'SYSTEM', 'View dashboard/module visibility coverage and smoke validation.'),
    ('VIEW_EXPORT_PACKAGE_READINESS_SUMMARY', 'View Export Package Readiness Summary', 'APPROVAL_WORKFLOW', 'View export package readiness, download status, and package metadata.'),
    ('VIEW_EXPORT_PACKAGE_EVIDENCE_DETAIL', 'View Export Package Evidence Detail', 'APPROVAL_WORKFLOW', 'View export package item and audit evidence details.'),
    ('VIEW_ACCOUNTING_RECONCILIATION_WORKBENCH', 'View Accounting Reconciliation Workbench', 'APPROVAL_WORKFLOW', 'View accounting reconciliation queues and exception groups.'),
    ('VIEW_LOCKED_PERIOD_AUDIT_EVIDENCE', 'View Locked Period Audit Evidence', 'APPROVAL_WORKFLOW', 'View locked/reconciled period audit evidence.'),
    ('VIEW_ROLE_ACCESS_MATRIX', 'View Role Access Matrix', 'SECURITY', 'View role-to-permission access matrix for module governance.'),
    ('VIEW_DEMO_READINESS_COMMAND_CENTER', 'View Demo Readiness Command Center', 'SYSTEM', 'View demo readiness, deployment health, and data readiness checks.'),
    ('VIEW_WORKFLOW_VALIDATION_RULES', 'View Workflow Validation Rules', 'APPROVAL_WORKFLOW', 'View workflow validation rules and current evidence.'),
    ('VIEW_WORKFLOW_OPERATIONS_CENTER', 'View Workflow Operations Center', 'APPROVAL_WORKFLOW', 'View combined workflow operations, audit, export, reconciliation, and validation status.'),
    ('RUN_WORKFLOW_DRY_RUN', 'Run Workflow Dry Run', 'APPROVAL_WORKFLOW', 'Run non-destructive workflow dry-run checks without changing time entry status.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

-- Broad read visibility for workflow operators and executives where appropriate.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
  ON p.permission_code IN (
      'VIEW_AUDIT_HISTORY_EVENTS',
      'VIEW_WORKFLOW_ACTION_CAPABILITIES',
      'VIEW_MODULE_VISIBILITY_SMOKE',
      'VIEW_EXPORT_PACKAGE_READINESS_SUMMARY',
      'VIEW_EXPORT_PACKAGE_EVIDENCE_DETAIL',
      'VIEW_LOCKED_PERIOD_AUDIT_EVIDENCE',
      'VIEW_DEMO_READINESS_COMMAND_CENTER',
      'VIEW_WORKFLOW_VALIDATION_RULES',
      'VIEW_WORKFLOW_OPERATIONS_CENTER'
  )
WHERE r.role_code IN (
    'ADMINISTRATOR',
    'PROJECT_TEAM_COORDINATOR',
    'ACCOUNTING',
    'PROJECT_MANAGEMENT',
    'PROJECT_MANAGER',
    'MANAGER',
    'EXECUTIVE'
)
ON CONFLICT DO NOTHING;

-- Accounting workbench remains accounting/PTC/admin focused.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
  ON p.permission_code IN (
      'VIEW_ACCOUNTING_RECONCILIATION_WORKBENCH',
      'RUN_WORKFLOW_DRY_RUN'
  )
WHERE r.role_code IN (
    'ADMINISTRATOR',
    'PROJECT_TEAM_COORDINATOR',
    'ACCOUNTING'
)
ON CONFLICT DO NOTHING;

-- Role matrix is administrator/PTC only.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
  ON p.permission_code = 'VIEW_ROLE_ACCESS_MATRIX'
WHERE r.role_code IN (
    'ADMINISTRATOR',
    'PROJECT_TEAM_COORDINATOR'
)
ON CONFLICT DO NOTHING;

CREATE TABLE IF NOT EXISTS dashboard_module_visibility_expectations (
    module_key text PRIMARY KEY,
    module_name text NOT NULL,
    route text NOT NULL,
    group_name text NOT NULL,
    required_permissions text[] NOT NULL DEFAULT ARRAY[]::text[],
    allowed_roles text[] NOT NULL DEFAULT ARRAY[]::text[],
    expected_visibility text NOT NULL DEFAULT 'role_scoped',
    notes text,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamp with time zone NOT NULL DEFAULT NOW(),
    updated_at timestamp with time zone NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS workflow_action_dry_run_events (
    workflow_action_dry_run_event_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    actor_user_id uuid NULL REFERENCES app_users(user_id),
    dry_run_action text NOT NULL,
    week_start date NULL,
    week_end date NULL,
    eligible_item_count integer NOT NULL DEFAULT 0,
    eligible_hours numeric(12, 2) NOT NULL DEFAULT 0,
    result_payload jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamp with time zone NOT NULL DEFAULT NOW()
);

INSERT INTO dashboard_module_visibility_expectations (
    module_key,
    module_name,
    route,
    group_name,
    required_permissions,
    allowed_roles,
    expected_visibility,
    notes
)
VALUES
    ('019M-AZ', 'Audit History Events', 'workflow', 'Approval / Export / Audit',
        ARRAY['VIEW_AUDIT_HISTORY_EVENTS', 'VIEW_WORKFLOW_AUDIT_EVIDENCE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR', 'ACCOUNTING', 'PROJECT_MANAGEMENT', 'PROJECT_MANAGER', 'MANAGER', 'EXECUTIVE'],
        'role_scoped',
        'Repairs audit history endpoint visibility and supports workflow audit review.'),
    ('019M-BA', 'Workflow Action Capabilities', 'workflow', 'Approval / Export / Audit',
        ARRAY['VIEW_WORKFLOW_ACTION_CAPABILITIES', 'PROJECT_TIME_APPROVAL', 'VIEW_ACCOUNT_RECONCILIATION', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR', 'ACCOUNTING', 'PROJECT_MANAGEMENT', 'PROJECT_MANAGER', 'MANAGER'],
        'role_scoped',
        'Shows allowed workflow actions and non-destructive dry-run readiness.'),
    ('019M-BB', 'Dashboard Module Visibility Smoke', 'dashboard', 'System',
        ARRAY['VIEW_MODULE_VISIBILITY_SMOKE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR'],
        'admin_operator',
        'Checks whether module cards are expected to appear by role and permission.'),
    ('019M-BC', 'Export Package Readiness Summary', 'workflow', 'Approval / Export / Audit',
        ARRAY['VIEW_EXPORT_PACKAGE_READINESS_SUMMARY', 'DOWNLOAD_TIME_EXPORT_PACKAGE', 'EXPORT_TIME_EXCEL', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR', 'ACCOUNTING'],
        'operator_only',
        'Summarizes export package readiness, downloads, and package status.'),
    ('019M-BD', 'Export Package Evidence Detail', 'workflow', 'Approval / Export / Audit',
        ARRAY['VIEW_EXPORT_PACKAGE_EVIDENCE_DETAIL', 'VIEW_WORKFLOW_AUDIT_EVIDENCE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR', 'ACCOUNTING'],
        'operator_only',
        'Links export packages to item evidence and audit records.'),
    ('019M-BE', 'Accounting Reconciliation Workbench', 'workflow', 'Approval / Export / Audit',
        ARRAY['VIEW_ACCOUNTING_RECONCILIATION_WORKBENCH', 'MANAGE_ACCOUNT_RECONCILIATION', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR', 'ACCOUNTING'],
        'operator_only',
        'Provides grouped accounting reconciliation queues and exception visibility.'),
    ('019M-BF', 'Locked Period Audit Evidence', 'workflow', 'Approval / Export / Audit',
        ARRAY['VIEW_LOCKED_PERIOD_AUDIT_EVIDENCE', 'VIEW_WORKFLOW_AUDIT_EVIDENCE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR', 'ACCOUNTING', 'EXECUTIVE'],
        'role_scoped',
        'Shows locked/reconciled period audit evidence.'),
    ('019M-BG', 'Role Access Matrix', 'role-admin', 'Security',
        ARRAY['VIEW_ROLE_ACCESS_MATRIX', 'VIEW_ROLE_ADMIN_DIRECTORY', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR'],
        'admin_operator',
        'Shows role-to-permission coverage for governance validation.'),
    ('019M-BH', 'Demo Readiness Command Center', 'dashboard', 'System',
        ARRAY['VIEW_DEMO_READINESS_COMMAND_CENTER', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR', 'EXECUTIVE'],
        'role_scoped',
        'Summarizes demo readiness across workflow, exports, projects, users, and audit evidence.'),
    ('019M-BI', 'Workflow Validation Rules', 'workflow', 'Approval / Export / Audit',
        ARRAY['VIEW_WORKFLOW_VALIDATION_RULES', 'VIEW_APPROVAL_WORKFLOW', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR', 'ACCOUNTING', 'PROJECT_MANAGEMENT', 'PROJECT_MANAGER', 'MANAGER', 'EXECUTIVE'],
        'role_scoped',
        'Documents workflow guardrails and current validation evidence.'),
    ('019M-BJ', 'Workflow Operations Center', 'workflow', 'Approval / Export / Audit',
        ARRAY['VIEW_WORKFLOW_OPERATIONS_CENTER', 'VIEW_APPROVAL_WORKFLOW', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR', 'ACCOUNTING', 'PROJECT_MANAGEMENT', 'PROJECT_MANAGER', 'MANAGER', 'EXECUTIVE'],
        'role_scoped',
        'Central registry for the combined workflow operations expansion.'),
    ('019M-BK', 'Sprint Automation Validation', 'dashboard', 'System',
        ARRAY['VIEW_MODULE_VISIBILITY_SMOKE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR'],
        'admin_operator',
        'Adds validation script coverage for module visibility and endpoint smoke testing.')
ON CONFLICT (module_key) DO UPDATE
SET module_name = EXCLUDED.module_name,
    route = EXCLUDED.route,
    group_name = EXCLUDED.group_name,
    required_permissions = EXCLUDED.required_permissions,
    allowed_roles = EXCLUDED.allowed_roles,
    expected_visibility = EXCLUDED.expected_visibility,
    notes = EXCLUDED.notes,
    is_active = TRUE,
    updated_at = NOW();

CREATE INDEX IF NOT EXISTS ix_dashboard_module_visibility_expectations_route
ON dashboard_module_visibility_expectations(route, group_name, is_active);

CREATE INDEX IF NOT EXISTS ix_workflow_action_dry_run_events_created
ON workflow_action_dry_run_events(created_at DESC);

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'ptp_app') THEN
        GRANT SELECT ON dashboard_module_visibility_expectations TO ptp_app;
        GRANT SELECT, INSERT ON workflow_action_dry_run_events TO ptp_app;
    END IF;

    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'projectpulse_app') THEN
        GRANT SELECT ON dashboard_module_visibility_expectations TO projectpulse_app;
        GRANT SELECT, INSERT ON workflow_action_dry_run_events TO projectpulse_app;
    END IF;
END $$;
