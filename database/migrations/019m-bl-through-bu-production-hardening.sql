-- 019M-BL through 019M-BU Production Hardening Sprint

INSERT INTO app_permissions (permission_code, permission_name, module_code, permission_description)
VALUES
    ('VIEW_WORKFLOW_PREFLIGHT_VALIDATION', 'View Workflow Preflight Validation', 'APPROVAL_WORKFLOW', 'View non-destructive production workflow preflight checks before approval, reconciliation, lock, and export operations.'),
    ('RUN_WORKFLOW_PREFLIGHT_VALIDATION', 'Run Workflow Preflight Validation', 'APPROVAL_WORKFLOW', 'Run non-destructive production workflow preflight validation without changing time entry status.'),
    ('VIEW_PRODUCTION_READINESS_COMMAND_CENTER', 'View Production Readiness Command Center', 'SYSTEM', 'View production readiness checks across users, projects, workflow, export, audit, and route governance.'),
    ('VIEW_ROUTE_PERMISSION_CONTRACTS', 'View Route Permission Contracts', 'SECURITY', 'View route-level permission contracts for production governance and role enforcement.'),
    ('VIEW_NAVIGATION_REGISTRY_INTEGRITY', 'View Navigation Registry Integrity', 'SYSTEM', 'View navigation and dashboard registry integrity checks for production modules.'),
    ('VIEW_PRODUCTION_EXPORT_EVIDENCE', 'View Production Export Evidence', 'APPROVAL_WORKFLOW', 'View production export package evidence, package status, item counts, download audit, and reconciliation readiness.'),
    ('VIEW_ACCOUNTING_PRODUCTION_QUEUE', 'View Accounting Production Queue', 'APPROVAL_WORKFLOW', 'View accounting production reconciliation queue and exception groups.'),
    ('VIEW_ENGINEER_NEGATIVE_ACCESS_SMOKE', 'View Engineer Negative Access Smoke', 'SECURITY', 'View production negative access checks confirming engineers are excluded from restricted workflow/export operations.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

-- Rename prior non-production wording where possible while preserving codes for compatibility.
UPDATE app_permissions
SET permission_name = 'Run Workflow Preflight Validation',
    permission_description = 'Run non-destructive production workflow preflight validation without changing time entry status.'
WHERE permission_code = 'RUN_WORKFLOW_DRY_RUN';

UPDATE app_permissions
SET permission_name = 'View Production Readiness Command Center',
    permission_description = 'View production readiness, deployment health, and operational readiness checks.'
WHERE permission_code = 'VIEW_DEMO_READINESS_COMMAND_CENTER';

-- Broad read visibility for production operational modules.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
  ON p.permission_code IN (
      'VIEW_WORKFLOW_PREFLIGHT_VALIDATION',
      'VIEW_PRODUCTION_READINESS_COMMAND_CENTER',
      'VIEW_NAVIGATION_REGISTRY_INTEGRITY',
      'VIEW_PRODUCTION_EXPORT_EVIDENCE',
      'VIEW_ACCOUNTING_PRODUCTION_QUEUE',
      'VIEW_ENGINEER_NEGATIVE_ACCESS_SMOKE'
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

-- Production preflight run remains operator/accounting/admin scoped.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
  ON p.permission_code IN (
      'RUN_WORKFLOW_PREFLIGHT_VALIDATION',
      'VIEW_ROUTE_PERMISSION_CONTRACTS'
  )
WHERE r.role_code IN (
    'ADMINISTRATOR',
    'PROJECT_TEAM_COORDINATOR',
    'ACCOUNTING'
)
ON CONFLICT DO NOTHING;

CREATE TABLE IF NOT EXISTS workflow_preflight_validation_events (
    workflow_preflight_validation_event_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    actor_user_id uuid NULL REFERENCES app_users(user_id),
    preflight_action text NOT NULL,
    week_start date NULL,
    week_end date NULL,
    eligible_item_count integer NOT NULL DEFAULT 0,
    eligible_hours numeric(12, 2) NOT NULL DEFAULT 0,
    blocked_item_count integer NOT NULL DEFAULT 0,
    issue_count integer NOT NULL DEFAULT 0,
    result_payload jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamp with time zone NOT NULL DEFAULT NOW()
);

-- Preserve evidence from the prior compatibility table when it exists.
INSERT INTO workflow_preflight_validation_events (
    actor_user_id,
    preflight_action,
    week_start,
    week_end,
    eligible_item_count,
    eligible_hours,
    blocked_item_count,
    issue_count,
    result_payload,
    created_at
)
SELECT
    actor_user_id,
    dry_run_action,
    week_start,
    week_end,
    eligible_item_count,
    eligible_hours,
    0,
    0,
    jsonb_set(
        COALESCE(result_payload, '{}'::jsonb),
        '{productionNamingRefactor}',
        'true'::jsonb,
        true
    ),
    created_at
FROM workflow_action_dry_run_events old_events
WHERE NOT EXISTS (
    SELECT 1
    FROM workflow_preflight_validation_events existing
    WHERE existing.actor_user_id IS NOT DISTINCT FROM old_events.actor_user_id
      AND existing.preflight_action = old_events.dry_run_action
      AND existing.week_start IS NOT DISTINCT FROM old_events.week_start
      AND existing.week_end IS NOT DISTINCT FROM old_events.week_end
      AND existing.created_at = old_events.created_at
);

CREATE TABLE IF NOT EXISTS route_permission_contracts (
    route_permission_contract_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    route_key text NOT NULL,
    route_path text NOT NULL,
    module_name text NOT NULL,
    module_group text NOT NULL,
    required_permissions text[] NOT NULL DEFAULT ARRAY[]::text[],
    allowed_roles text[] NOT NULL DEFAULT ARRAY[]::text[],
    restricted_roles text[] NOT NULL DEFAULT ARRAY[]::text[],
    contract_status text NOT NULL DEFAULT 'active',
    production_guardrail text NOT NULL DEFAULT '',
    created_at timestamp with time zone NOT NULL DEFAULT NOW(),
    updated_at timestamp with time zone NOT NULL DEFAULT NOW(),
    UNIQUE(route_key, module_name)
);

INSERT INTO route_permission_contracts (
    route_key,
    route_path,
    module_name,
    module_group,
    required_permissions,
    allowed_roles,
    restricted_roles,
    contract_status,
    production_guardrail
)
VALUES
    ('dashboard', '#dashboard', 'Production Readiness Command Center', 'System',
        ARRAY['VIEW_PRODUCTION_READINESS_COMMAND_CENTER', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR', 'EXECUTIVE'],
        ARRAY['ENGINEER'],
        'active',
        'Production readiness is visible to leadership and operators; engineers do not receive restricted operational controls.'),
    ('workflow', '#workflow', 'Workflow Preflight Validation', 'Approval / Export / Audit',
        ARRAY['VIEW_WORKFLOW_PREFLIGHT_VALIDATION', 'VIEW_APPROVAL_WORKFLOW', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR', 'ACCOUNTING', 'PROJECT_MANAGEMENT', 'PROJECT_MANAGER', 'MANAGER'],
        ARRAY['ENGINEER'],
        'active',
        'Preflight validation is non-destructive and must not change workflow status.'),
    ('workflow', '#workflow', 'Accounting Production Queue', 'Approval / Export / Audit',
        ARRAY['VIEW_ACCOUNTING_PRODUCTION_QUEUE', 'MANAGE_ACCOUNT_RECONCILIATION', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR', 'ACCOUNTING'],
        ARRAY['ENGINEER', 'PROJECT_MANAGER', 'PROJECT_MANAGEMENT'],
        'active',
        'Accounting queue is restricted to accounting and production operators.'),
    ('workflow', '#workflow', 'Production Export Evidence', 'Approval / Export / Audit',
        ARRAY['VIEW_PRODUCTION_EXPORT_EVIDENCE', 'DOWNLOAD_TIME_EXPORT_PACKAGE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR', 'ACCOUNTING'],
        ARRAY['ENGINEER'],
        'active',
        'Export evidence and package downloads remain restricted to export-enabled roles.'),
    ('role-admin', '#role-admin', 'Route Permission Contracts', 'Security',
        ARRAY['VIEW_ROUTE_PERMISSION_CONTRACTS', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR'],
        ARRAY['ENGINEER', 'MANAGER', 'PROJECT_MANAGER', 'PROJECT_MANAGEMENT', 'ACCOUNTING', 'EXECUTIVE'],
        'active',
        'Route contract governance is limited to administrators and platform operators.'),
    ('dashboard', '#dashboard', 'Navigation Registry Integrity Guard', 'System',
        ARRAY['VIEW_NAVIGATION_REGISTRY_INTEGRITY', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR'],
        ARRAY['ENGINEER'],
        'active',
        'Navigation registry integrity confirms production modules are reachable and role-scoped.'),
    ('dashboard', '#dashboard', 'Engineer Negative Access Smoke', 'Security',
        ARRAY['VIEW_ENGINEER_NEGATIVE_ACCESS_SMOKE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR'],
        ARRAY['ENGINEER'],
        'active',
        'Negative access smoke confirms restricted endpoints remain denied for engineer-only users.')
ON CONFLICT (route_key, module_name) DO UPDATE
SET route_path = EXCLUDED.route_path,
    module_group = EXCLUDED.module_group,
    required_permissions = EXCLUDED.required_permissions,
    allowed_roles = EXCLUDED.allowed_roles,
    restricted_roles = EXCLUDED.restricted_roles,
    contract_status = EXCLUDED.contract_status,
    production_guardrail = EXCLUDED.production_guardrail,
    updated_at = NOW();

-- Update dashboard module expectations to production naming.
UPDATE dashboard_module_visibility_expectations
SET module_name = 'Production Readiness Command Center',
    required_permissions = ARRAY['VIEW_PRODUCTION_READINESS_COMMAND_CENTER', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    notes = 'Summarizes production readiness across workflow, exports, projects, users, audit evidence, and route governance.',
    updated_at = NOW()
WHERE module_name = 'Demo Readiness Command Center'
   OR module_key = '019M-BH';

UPDATE dashboard_module_visibility_expectations
SET module_name = 'Workflow Preflight Validation',
    required_permissions = ARRAY['VIEW_WORKFLOW_PREFLIGHT_VALIDATION', 'RUN_WORKFLOW_PREFLIGHT_VALIDATION', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
    notes = 'Runs production-safe non-destructive workflow preflight validation before reconciliation, lock, and export operations.',
    updated_at = NOW()
WHERE module_name = 'Workflow Action Capabilities'
   OR module_key = '019M-BA';

UPDATE dashboard_module_visibility_expectations
SET module_name = 'Production Validation Automation',
    notes = 'Provides production validation script coverage for endpoint smoke checks, dashboard registry verification, and access enforcement.',
    updated_at = NOW()
WHERE module_name = 'Sprint Automation Validation'
   OR module_key = '019M-BK';

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
    ('019M-BL', 'Production Naming Refactor', 'dashboard', 'System',
        ARRAY['VIEW_NAVIGATION_REGISTRY_INTEGRITY', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR'],
        'admin_operator',
        'Removes demo/dry-run language from production-facing registry and replaces it with production readiness and preflight terminology.'),
    ('019M-BM', 'Workflow Preflight Validation', 'workflow', 'Approval / Export / Audit',
        ARRAY['VIEW_WORKFLOW_PREFLIGHT_VALIDATION', 'RUN_WORKFLOW_PREFLIGHT_VALIDATION', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR', 'ACCOUNTING'],
        'operator_only',
        'Runs non-destructive production workflow preflight validation before approval, reconciliation, lock, and export operations.'),
    ('019M-BN', 'Production Readiness Command Center', 'dashboard', 'System',
        ARRAY['VIEW_PRODUCTION_READINESS_COMMAND_CENTER', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR', 'EXECUTIVE'],
        'role_scoped',
        'Shows production readiness across users, projects, workflow, exports, audit evidence, and route governance.'),
    ('019M-BR', 'Route Permission Contracts', 'role-admin', 'Security',
        ARRAY['VIEW_ROUTE_PERMISSION_CONTRACTS', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR'],
        'admin_operator',
        'Defines route-level permission contracts and restricted roles for production governance.'),
    ('019M-BT', 'Navigation Registry Integrity Guard', 'dashboard', 'System',
        ARRAY['VIEW_NAVIGATION_REGISTRY_INTEGRITY', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR'],
        'admin_operator',
        'Validates navigation and dashboard registry coverage for production modules.'),
    ('019M-BU', 'Production Export Evidence Expansion', 'workflow', 'Approval / Export / Audit',
        ARRAY['VIEW_PRODUCTION_EXPORT_EVIDENCE', 'DOWNLOAD_TIME_EXPORT_PACKAGE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL'],
        ARRAY['ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR', 'ACCOUNTING'],
        'operator_only',
        'Expands production export evidence with package, download, audit, and readiness status.')
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

CREATE INDEX IF NOT EXISTS ix_workflow_preflight_validation_events_created
ON workflow_preflight_validation_events(created_at DESC);

CREATE INDEX IF NOT EXISTS ix_workflow_preflight_validation_events_action
ON workflow_preflight_validation_events(preflight_action, week_start, week_end);

CREATE INDEX IF NOT EXISTS ix_route_permission_contracts_route
ON route_permission_contracts(route_key, contract_status);

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'ptp_app') THEN
        GRANT SELECT, INSERT ON workflow_preflight_validation_events TO ptp_app;
        GRANT SELECT ON route_permission_contracts TO ptp_app;
    END IF;

    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'projectpulse_app') THEN
        GRANT SELECT, INSERT ON workflow_preflight_validation_events TO projectpulse_app;
        GRANT SELECT ON route_permission_contracts TO projectpulse_app;
    END IF;
END $$;
