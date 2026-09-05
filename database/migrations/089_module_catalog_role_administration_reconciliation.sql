-- Pulse migration 089
-- Reconcile the complete built-in module catalog with Modules 012/037 and
-- restore Module 001A Engineer Request Closeout access for Engineer and
-- Engineering Lead roles.
--
-- This migration is additive, idempotent, and preserves every unrelated
-- published role-policy decision. It never widens Module 001A data beyond the
-- backend-enforced authenticated Engineer's own eligible assignments.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

DO $projectpulse089_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.app_roles') IS NULL
       OR to_regclass('public.app_permissions') IS NULL
       OR to_regclass('public.app_role_permissions') IS NULL
       OR to_regclass('public.scoped_role_policy_modules') IS NULL
       OR to_regclass('public.scoped_role_policy_actions') IS NULL
       OR to_regclass('public.scoped_role_policy_scopes') IS NULL
       OR to_regclass('public.scoped_role_policy_versions') IS NULL
       OR to_regclass('public.scoped_role_policy_grants') IS NULL THEN
        RAISE EXCEPTION 'Migration 089 requires the application RBAC and scoped role-policy foundations.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM schema_migrations
        WHERE migration_id = '078_module_001a_engineer_request_closeout'
    ) THEN
        RAISE EXCEPTION 'Migration 089 requires Module 001A migration 078 first.';
    END IF;
END;
$projectpulse089_prerequisites$;

CREATE TABLE IF NOT EXISTS module_catalog_reconciliation_089_modules (
    module_code TEXT PRIMARY KEY,
    was_present BOOLEAN NOT NULL,
    previous_module_name TEXT NULL,
    previous_route_scope TEXT NULL,
    previous_current_state TEXT NULL,
    previous_permission_notes TEXT NULL,
    previous_source_url TEXT NULL,
    previous_is_active BOOLEAN NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS module_catalog_reconciliation_089_permission_grants (
    app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE RESTRICT,
    app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    role_code TEXT NOT NULL,
    permission_code TEXT NOT NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (app_role_id, app_permission_id)
);

CREATE TABLE IF NOT EXISTS module_catalog_reconciliation_089_policy_versions (
    singleton_key BOOLEAN PRIMARY KEY DEFAULT TRUE CHECK (singleton_key),
    previous_policy_version_id UUID NOT NULL REFERENCES scoped_role_policy_versions(policy_version_id) ON DELETE RESTRICT,
    replacement_policy_version_id UUID NOT NULL REFERENCES scoped_role_policy_versions(policy_version_id) ON DELETE RESTRICT,
    previous_version_number INTEGER NOT NULL,
    replacement_version_number INTEGER NOT NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TEMP TABLE projectpulse_089_module_catalog (
    module_code TEXT PRIMARY KEY,
    module_name TEXT NOT NULL,
    route_scope TEXT NOT NULL,
    module_group TEXT NOT NULL
) ON COMMIT DROP;

INSERT INTO projectpulse_089_module_catalog (
    module_code,
    module_name,
    route_scope,
    module_group
)
VALUES
    ('001', 'Timesheet', 'timesheet', 'Time Management'),
    ('001A', 'Engineer Request Closeout', 'engineer-task-closeout', 'Time Management'),
    ('002', 'Approval Inbox', 'manager-approval', 'Approvals'),
    ('003', 'Utilization', 'utilization', 'Resource Management'),
    ('004', 'Holiday Administration', 'holiday-admin', 'Time Management'),
    ('005', 'Project Expense Upload', 'project-allocation-info', 'Project Management'),
    ('006', 'Toyota & Hyundai Pipelines', 'toyota-hyundai-pipelines', 'Sales & Opportunities'),
    ('007', 'Approval, Export & Audit Workflow', 'workflow', 'Approvals'),
    ('008', 'Audit History', 'audit-history', 'Security & Audit'),
    ('009', 'User Administration', 'user-admin', 'Administration'),
    ('010', 'Azure / Entra Directory Users', 'azure-admin', 'Administration'),
    ('011', 'Celar AI', 'work-task-builder', 'AI & Automation'),
    ('012', 'Role Administration', 'role-admin', 'Administration'),
    ('013', 'System Health & API Diagnostics', 'service-control', 'Platform Operations'),
    ('014', 'Backup & Disaster Recovery', 'backup-dr', 'Platform Operations'),
    ('015', 'Restore Validation', 'restore-validation', 'Platform Operations'),
    ('016', 'Operational Evidence & Backup Retention', 'backup-retention', 'Platform Operations'),
    ('017', 'Replication & Sync', 'replication-sync', 'Platform Operations'),
    ('018', 'Project Workload', 'project-workload', 'Project Management'),
    ('019', 'Project Engineering Workspace', 'project-workspace', 'Project Delivery'),
    ('020', 'Project Intake & Resource Handoff', 'project-intake', 'Project Delivery'),
    ('021', 'Customer Directory', 'customer-directory', 'Customers'),
    ('022', 'Cost Alerts', 'cost-alerts', 'Reports & Workflow'),
    ('023', 'Time Compliance', 'time-compliance', 'Time Management'),
    ('024', 'Sales Intake', 'sales-intake', 'Sales & Opportunities'),
    ('025', 'SOW & GSD Workspace', 'sow-generator', 'Sales & Opportunities'),
    ('026', 'CRM / ERP Integration Center', 'crm-integration', 'Integrations'),
    ('027', 'Signed Handoff', 'signed-handoff', 'Project Delivery'),
    ('028', 'AI Time Entry', 'ai-time-entry', 'Time Management'),
    ('029', 'UAT Validation', 'uat-validation', 'Platform Operations'),
    ('030', 'Analytics Center', 'reporting', 'Reports & Workflow'),
    ('031', 'Financial Operations Workbench', 'financial-operations-workbench', 'Reports & Workflow'),
    ('032', 'Notification Delivery Monitor', 'notification-delivery-monitor', 'Reports & Workflow'),
    ('033', 'Project Forge', 'project-forge', 'Project Delivery'),
    ('036', 'Sales Insights Dashboard', 'sales-insights', 'Sales & Opportunities'),
    ('037', 'Roles & Permissions Matrix', 'roles-permissions-matrix', 'Administration'),
    ('038', 'Certify Connection & Sync Center', 'certify-integration', 'Integrations'),
    ('039', 'Billing Readiness', 'billing-readiness', 'Reports & Workflow'),
    ('040', 'Project Closeout', 'project-closeout', 'Reports & Workflow'),
    ('041', 'Closeout Email Automation', 'closeout-email', 'Reports & Workflow'),
    ('042', 'Invoice & Billing Center', 'invoice-billing-center', 'Reports & Workflow'),
    ('055B', 'Rate Card Administration', 'rate-card-administration', 'Project Operations'),
    ('055C', 'Manage Existing Projects', 'work-register', 'Project Operations'),
    ('055D', 'Create New Project', 'create-work-register', 'Project Operations'),
    ('057', 'Calendar & Capacity', 'calendar-capacity', 'Resource Management'),
    ('058', 'CI/CD Pipeline', 'cicd-pipeline', 'Platform Operations'),
    ('060', 'Contracts', 'contracts', 'Project Operations'),
    ('063', 'Opportunities', 'opportunities', 'Sales & Opportunities'),
    ('064', 'AI Provider Configuration Center', 'ai-provider-configuration', 'Security'),
    ('065', 'Microsoft Integration Connection', 'entra-secret-administration', 'Integrations'),
    ('066', 'Project FlowHive', 'project-flowhive', 'Project Delivery'),
    ('067', 'Global Mail Configuration Center', 'global-mail-configuration', 'Platform Operations'),
    ('068', 'Provider-Neutral System Architecture', 'system-architecture', 'Platform Operations'),
    ('069', 'Qualifications & Certification Matrix', 'qualifications-certifications', 'Resources'),
    ('070', 'Capacity & Pipeline Forecasting', 'capacity-pipeline-forecast', 'Resource Management'),
    ('071', 'On-Call Scheduling', 'oncall-scheduling', 'Platform Operations'),
    ('072', 'OneAssist Routing Directory', 'oneassist-routing-directory', 'Platform Operations'),
    ('073', 'Sales Coverage Alignment', 'sales-coverage-alignment', 'Sales & Opportunities'),
    ('074', 'OEM & Vendor Directory', 'oem-vendor-directory', 'Sales & Opportunities'),
    ('075', 'Integration Automation & Event Gateway', 'integration-event-gateway', 'Platform Operations'),
    ('076', 'Defect Intake & Resolution Tracker', 'defect-tracker', 'Help & Documentation'),
    ('077', 'Release, Deployment & Rollback Control Center', 'release-deployment-control', 'Platform Operations'),
    ('078', 'Observability, SLO & Application Health Center', 'observability-slo-health', 'Platform Operations'),
    ('079', 'Data Governance, Retention & Privacy Center', 'data-governance-retention', 'Security & Audit'),
    ('080', 'Customer Delivery & Acceptance Portal', 'customer-delivery-acceptance', 'Project Operations'),
    ('081', 'Lab Equipment Tracker', 'lab-equipment-tracker', 'Platform Operations'),
    ('082', 'Enterprise Project Risk Register', 'project-risk-register', 'Project Delivery'),
    ('083', 'Full Future Loop', 'full-future-loop', 'Platform Operations'),
    ('084', 'Celar AI Runtime & Version Center', 'celar-ai-runtime-version', 'Platform Operations'),
    ('997', 'Security Operations, Threat Intelligence & Response Center', 'security-operations', 'Security & Audit'),
    ('998', 'System Diagnostic & Controlled Remediation Center', 'system-diagnostics', 'Platform Operations'),
    ('999', 'System User Guide', 'user-guide', 'Help & Documentation');

INSERT INTO module_catalog_reconciliation_089_modules (
    module_code,
    was_present,
    previous_module_name,
    previous_route_scope,
    previous_current_state,
    previous_permission_notes,
    previous_source_url,
    previous_is_active
)
SELECT
    desired.module_code,
    existing.module_code IS NOT NULL,
    existing.module_name,
    existing.route_scope,
    existing.current_state,
    existing.permission_notes,
    existing.source_url,
    existing.is_active
FROM projectpulse_089_module_catalog desired
LEFT JOIN scoped_role_policy_modules existing
  ON upper(existing.module_code) = upper(desired.module_code)
ON CONFLICT (module_code) DO NOTHING;

INSERT INTO scoped_role_policy_modules (
    module_code,
    module_name,
    route_scope,
    current_state,
    permission_notes,
    source_url,
    is_active
)
SELECT
    module_code,
    module_name,
    route_scope,
    'Installed',
    'Canonical Pulse module catalog entry · ' || module_group || '. Synchronized by migration 089.',
    'src/frontend/project-time-web/src/module-availability-registry.js',
    TRUE
FROM projectpulse_089_module_catalog
ON CONFLICT (module_code) DO UPDATE
SET module_name = EXCLUDED.module_name,
    route_scope = EXCLUDED.route_scope,
    current_state = EXCLUDED.current_state,
    permission_notes = EXCLUDED.permission_notes,
    source_url = EXCLUDED.source_url,
    is_active = TRUE;

CREATE TEMP TABLE projectpulse_089_role_permissions (
    role_code TEXT NOT NULL,
    permission_code TEXT NOT NULL,
    PRIMARY KEY (role_code, permission_code)
) ON COMMIT DROP;

INSERT INTO projectpulse_089_role_permissions (role_code, permission_code)
VALUES
    ('SUPER_ADMINISTRATOR', 'VIEW_ENGINEER_TASK_CLOSEOUT_001A'),
    ('SUPER_ADMINISTRATOR', 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A'),
    ('ENGINEER', 'VIEW_ENGINEER_TASK_CLOSEOUT_001A'),
    ('ENGINEER', 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A'),
    ('ENGINEERING', 'VIEW_ENGINEER_TASK_CLOSEOUT_001A'),
    ('ENGINEERING', 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A'),
    ('ENGINEERING_LEAD', 'VIEW_ENGINEER_TASK_CLOSEOUT_001A'),
    ('ENGINEERING_LEAD', 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A'),
    ('ENGINEERING_TEAM_LEAD', 'VIEW_ENGINEER_TASK_CLOSEOUT_001A'),
    ('ENGINEERING_TEAM_LEAD', 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A');

INSERT INTO module_catalog_reconciliation_089_permission_grants (
    app_role_id,
    app_permission_id,
    role_code,
    permission_code
)
SELECT
    role.app_role_id,
    permission.app_permission_id,
    upper(role.role_code),
    permission.permission_code
FROM projectpulse_089_role_permissions desired
JOIN app_roles role
  ON upper(role.role_code) = desired.role_code
 AND role.is_active = TRUE
JOIN app_permissions permission
  ON upper(permission.permission_code) = desired.permission_code
LEFT JOIN app_role_permissions existing
  ON existing.app_role_id = role.app_role_id
 AND existing.app_permission_id = permission.app_permission_id
WHERE existing.app_role_permission_id IS NULL
ON CONFLICT (app_role_id, app_permission_id) DO NOTHING;

INSERT INTO app_role_permissions (
    app_role_id,
    app_permission_id,
    created_at
)
SELECT
    app_role_id,
    app_permission_id,
    NOW()
FROM module_catalog_reconciliation_089_permission_grants
ON CONFLICT (app_role_id, app_permission_id) DO NOTHING;

CREATE TEMP TABLE projectpulse_089_module001a_grants (
    role_code TEXT NOT NULL,
    action_code TEXT NOT NULL,
    scope_code TEXT NOT NULL,
    reason_required BOOLEAN NOT NULL DEFAULT FALSE,
    PRIMARY KEY (role_code, action_code, scope_code)
) ON COMMIT DROP;

INSERT INTO projectpulse_089_module001a_grants (
    role_code,
    action_code,
    scope_code,
    reason_required
)
VALUES
    ('ENGINEERING', 'MODULE_ACCESS', 'ORGANIZATION', FALSE),
    ('ENGINEERING', 'MODULE_VIEW', 'SELF', FALSE),
    ('ENGINEERING', 'RECORD_CREATE', 'SELF', FALSE),
    ('ENGINEERING', 'RECORD_EDIT', 'SELF', FALSE),
    ('ENGINEERING', 'RECORD_REOPEN', 'SELF', TRUE),
    ('ENGINEERING', 'WORKFLOW_MANAGE', 'SELF', FALSE),
    ('ENGINEERING', 'AUDIT_VIEW', 'SELF', FALSE),
    ('ENGINEERING_LEAD', 'MODULE_ACCESS', 'ORGANIZATION', FALSE),
    ('ENGINEERING_LEAD', 'MODULE_VIEW', 'SELF', FALSE),
    ('ENGINEERING_LEAD', 'RECORD_CREATE', 'SELF', FALSE),
    ('ENGINEERING_LEAD', 'RECORD_EDIT', 'SELF', FALSE),
    ('ENGINEERING_LEAD', 'RECORD_REOPEN', 'SELF', TRUE),
    ('ENGINEERING_LEAD', 'WORKFLOW_MANAGE', 'SELF', FALSE),
    ('ENGINEERING_LEAD', 'AUDIT_VIEW', 'SELF', FALSE);

DO $projectpulse089_validate_catalog_dependencies$
DECLARE
    missing_actions INTEGER;
    missing_scopes INTEGER;
BEGIN
    SELECT COUNT(*)
    INTO missing_actions
    FROM (
        SELECT DISTINCT action_code
        FROM projectpulse_089_module001a_grants
    ) desired
    LEFT JOIN scoped_role_policy_actions action
      ON upper(action.action_code) = upper(desired.action_code)
     AND action.is_active = TRUE
    WHERE action.action_code IS NULL;

    IF missing_actions <> 0 THEN
        RAISE EXCEPTION 'Migration 089 cannot publish Module 001A grants because % scoped action(s) are unavailable.', missing_actions;
    END IF;

    SELECT COUNT(*)
    INTO missing_scopes
    FROM (
        SELECT DISTINCT scope_code
        FROM projectpulse_089_module001a_grants
    ) desired
    LEFT JOIN scoped_role_policy_scopes scope
      ON upper(scope.scope_code) = upper(desired.scope_code)
     AND scope.is_active = TRUE
    WHERE scope.scope_code IS NULL;

    IF missing_scopes <> 0 THEN
        RAISE EXCEPTION 'Migration 089 cannot publish Module 001A grants because % scoped data scope(s) are unavailable.', missing_scopes;
    END IF;
END;
$projectpulse089_validate_catalog_dependencies$;

DO $projectpulse089_publish_policy$
DECLARE
    previous_id UUID;
    previous_number INTEGER;
    replacement_id UUID;
    replacement_number INTEGER;
    recorded_replacement_id UUID;
    missing_grants INTEGER;
    conflicting_denies INTEGER;
BEGIN
    SELECT replacement_policy_version_id
    INTO recorded_replacement_id
    FROM module_catalog_reconciliation_089_policy_versions
    WHERE singleton_key = TRUE;

    IF recorded_replacement_id IS NOT NULL THEN
        RETURN;
    END IF;

    SELECT policy_version_id, version_number
    INTO previous_id, previous_number
    FROM scoped_role_policy_versions
    WHERE policy_status = 'PUBLISHED'
    ORDER BY version_number DESC
    LIMIT 1;

    IF previous_id IS NULL THEN
        RAISE EXCEPTION 'Migration 089 requires one published scoped role-policy version.';
    END IF;

    SELECT COUNT(*)
    INTO missing_grants
    FROM projectpulse_089_module001a_grants desired
    LEFT JOIN scoped_role_policy_grants existing
      ON existing.policy_version_id = previous_id
     AND upper(existing.role_code) = desired.role_code
     AND upper(existing.module_code) = '001A'
     AND upper(existing.action_code) = desired.action_code
     AND upper(existing.scope_code) = desired.scope_code
     AND upper(existing.grant_effect) = 'GRANT'
     AND existing.is_active = TRUE
    WHERE existing.scoped_role_policy_grant_id IS NULL;

    SELECT COUNT(*)
    INTO conflicting_denies
    FROM scoped_role_policy_grants
    WHERE policy_version_id = previous_id
      AND upper(role_code) IN ('ENGINEERING', 'ENGINEERING_LEAD')
      AND upper(module_code) = '001A'
      AND upper(grant_effect) = 'DENY'
      AND is_active = TRUE;

    IF missing_grants = 0 AND conflicting_denies = 0 THEN
        RETURN;
    END IF;

    SELECT COALESCE(MAX(version_number), 0) + 1
    INTO replacement_number
    FROM scoped_role_policy_versions;

    replacement_id := gen_random_uuid();

    INSERT INTO scoped_role_policy_versions (
        policy_version_id,
        version_number,
        policy_name,
        policy_status,
        source_name,
        source_sha256,
        policy_notes,
        created_by_user_id,
        published_by_user_id,
        created_at
    )
    SELECT
        replacement_id,
        replacement_number,
        policy_name || ' · Module 001A access reconciliation',
        'DRAFT',
        'migration_089_module_catalog_role_administration_reconciliation',
        encode(digest('migration-089:' || replacement_number::text, 'sha256'), 'hex'),
        concat_ws(
            ' ',
            NULLIF(policy_notes, ''),
            'Migration 089 synchronized every built-in Pulse module into Role Administration and granted Engineer/Engineering Lead access to Module 001A while preserving backend self-scope enforcement.'
        ),
        created_by_user_id,
        published_by_user_id,
        NOW()
    FROM scoped_role_policy_versions
    WHERE policy_version_id = previous_id;

    INSERT INTO scoped_role_policy_grants (
        policy_version_id,
        role_code,
        module_code,
        action_code,
        scope_code,
        grant_effect,
        conditions,
        delegated_authority,
        reason_required,
        audit_required,
        source_designation,
        source_notes,
        is_active,
        created_at
    )
    SELECT
        replacement_id,
        role_code,
        module_code,
        action_code,
        scope_code,
        grant_effect,
        conditions,
        delegated_authority,
        reason_required,
        audit_required,
        source_designation,
        source_notes,
        is_active,
        NOW()
    FROM scoped_role_policy_grants
    WHERE policy_version_id = previous_id
      AND NOT (
          upper(module_code) = '001A'
          AND upper(role_code) IN ('ENGINEERING', 'ENGINEER', 'ENGINEERING_LEAD', 'ENGINEERING_TEAM_LEAD')
          AND (
              upper(action_code) = 'MODULE_ACCESS'
              OR upper(grant_effect) = 'DENY'
          )
      );

    INSERT INTO scoped_role_policy_grants (
        policy_version_id,
        role_code,
        module_code,
        action_code,
        scope_code,
        grant_effect,
        conditions,
        delegated_authority,
        reason_required,
        audit_required,
        source_designation,
        source_notes,
        is_active,
        created_at
    )
    SELECT
        replacement_id,
        desired.role_code,
        '001A',
        desired.action_code,
        desired.scope_code,
        'GRANT',
        jsonb_build_object(
            'source', 'Migration 089 Module 001A Role Administration reconciliation',
            'designation', 'Manage',
            'permissionLevel', 'Manage',
            'scopeCode', desired.scope_code,
            'moduleScopedOnly', FALSE,
            'engineerOwnedOnly', TRUE,
            'allowedWorkTypes', jsonb_build_array('SERVICE_REQUEST', 'PRESALES', 'INTERNAL')
        ),
        FALSE,
        desired.reason_required,
        desired.action_code <> 'MODULE_VIEW',
        'Manage',
        'Engineer-owned Service Request, Pre-Sales, and Internal closeout. Server endpoints remain limited to the authenticated Engineer''s own assignment.',
        TRUE,
        NOW()
    FROM projectpulse_089_module001a_grants desired
    ON CONFLICT (
        policy_version_id,
        role_code,
        module_code,
        action_code,
        scope_code,
        grant_effect
    ) DO NOTHING;

    INSERT INTO module_catalog_reconciliation_089_policy_versions (
        singleton_key,
        previous_policy_version_id,
        replacement_policy_version_id,
        previous_version_number,
        replacement_version_number
    )
    VALUES (
        TRUE,
        previous_id,
        replacement_id,
        previous_number,
        replacement_number
    )
    ON CONFLICT (singleton_key) DO NOTHING;

    UPDATE scoped_role_policy_versions
    SET policy_status = 'RETIRED',
        retired_at = NOW()
    WHERE policy_version_id = previous_id;

    UPDATE scoped_role_policy_versions
    SET policy_status = 'PUBLISHED',
        published_at = NOW()
    WHERE policy_version_id = replacement_id;

    IF to_regclass('public.scoped_role_policy_audit_events') IS NOT NULL THEN
        INSERT INTO scoped_role_policy_audit_events (
            policy_version_id,
            event_code,
            actor_user_id,
            actor_email,
            reason,
            previous_state,
            new_state,
            event_metadata
        )
        VALUES (
            replacement_id,
            'MIGRATION_089_MODULE001A_ROLE_CATALOG_RECONCILED',
            NULL,
            'migration-089@pulse.local',
            'Synchronize built-in modules and restore Module 001A access for Engineer and Engineering Lead.',
            jsonb_build_object(
                'policyVersionId', previous_id,
                'versionNumber', previous_number
            ),
            jsonb_build_object(
                'policyVersionId', replacement_id,
                'versionNumber', replacement_number
            ),
            jsonb_build_object(
                'moduleCode', '001A',
                'roles', jsonb_build_array('ENGINEERING', 'ENGINEERING_LEAD'),
                'permissionLevel', 'Manage',
                'scope', 'SELF',
                'catalogModuleCount', (SELECT COUNT(*) FROM projectpulse_089_module_catalog),
                'immutableAudit', TRUE
            )
        );
    END IF;
END;
$projectpulse089_publish_policy$;

DO $projectpulse089_assertions$
DECLARE
    catalog_mismatches INTEGER;
    permission_mismatches INTEGER;
    policy_mismatches INTEGER;
    conflicting_denies INTEGER;
    published_id UUID;
BEGIN
    SELECT COUNT(*)
    INTO catalog_mismatches
    FROM projectpulse_089_module_catalog desired
    LEFT JOIN scoped_role_policy_modules actual
      ON upper(actual.module_code) = upper(desired.module_code)
    WHERE actual.module_code IS NULL
       OR actual.module_name IS DISTINCT FROM desired.module_name
       OR actual.route_scope IS DISTINCT FROM desired.route_scope
       OR actual.is_active IS DISTINCT FROM TRUE;

    IF catalog_mismatches <> 0 THEN
        RAISE EXCEPTION 'Migration 089 invariant failed: % built-in module catalog row(s) are missing or inconsistent.', catalog_mismatches;
    END IF;

    SELECT COUNT(*)
    INTO permission_mismatches
    FROM projectpulse_089_role_permissions desired
    JOIN app_roles role
      ON upper(role.role_code) = desired.role_code
     AND role.is_active = TRUE
    LEFT JOIN app_permissions permission
      ON upper(permission.permission_code) = desired.permission_code
    LEFT JOIN app_role_permissions relationship
      ON relationship.app_role_id = role.app_role_id
     AND relationship.app_permission_id = permission.app_permission_id
    WHERE permission.app_permission_id IS NULL
       OR relationship.app_role_permission_id IS NULL;

    IF permission_mismatches <> 0 THEN
        RAISE EXCEPTION 'Migration 089 invariant failed: % Module 001A application-role permission relationship(s) are missing.', permission_mismatches;
    END IF;

    SELECT policy_version_id
    INTO published_id
    FROM scoped_role_policy_versions
    WHERE policy_status = 'PUBLISHED'
    ORDER BY version_number DESC
    LIMIT 1;

    SELECT COUNT(*)
    INTO policy_mismatches
    FROM projectpulse_089_module001a_grants desired
    LEFT JOIN scoped_role_policy_grants actual
      ON actual.policy_version_id = published_id
     AND upper(actual.role_code) = desired.role_code
     AND upper(actual.module_code) = '001A'
     AND upper(actual.action_code) = desired.action_code
     AND upper(actual.scope_code) = desired.scope_code
     AND upper(actual.grant_effect) = 'GRANT'
     AND actual.is_active = TRUE
    WHERE actual.scoped_role_policy_grant_id IS NULL;

    SELECT COUNT(*)
    INTO conflicting_denies
    FROM scoped_role_policy_grants
    WHERE policy_version_id = published_id
      AND upper(role_code) IN ('ENGINEERING', 'ENGINEERING_LEAD')
      AND upper(module_code) = '001A'
      AND upper(grant_effect) = 'DENY'
      AND is_active = TRUE;

    IF policy_mismatches <> 0 OR conflicting_denies <> 0 THEN
        RAISE EXCEPTION
            'Migration 089 invariant failed: % required Module 001A grant(s) are missing and % conflicting deny row(s) remain.',
            policy_mismatches,
            conflicting_denies;
    END IF;
END;
$projectpulse089_assertions$;

INSERT INTO schema_migrations (
    migration_id,
    description,
    applied_at
)
VALUES (
    '089_module_catalog_role_administration_reconciliation',
    'Synchronize all built-in Pulse modules into Role Administration and restore Module 001A Engineer/Engineering Lead access',
    NOW()
)
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
