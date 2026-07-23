-- ProjectPulse Modules 012/037 scoped RBAC foundation.
-- Generated from ProjectPulse_Module_Role_Permissions_Matrix(2).xlsx.
-- Apply after 039_work_to_cash_reactivation_lock_order.sql.
-- Additive and backward compatible: legacy RBAC tables and user-role assignments
-- remain authoritative whenever a workbook cell is Not Set.
BEGIN;

DO $projectpulse040_prerequisite$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id = '039_work_to_cash_reactivation_lock_order'
    ) THEN
        RAISE EXCEPTION
            'Migration 040 requires 039_work_to_cash_reactivation_lock_order first.';
    END IF;
END;
$projectpulse040_prerequisite$;

CREATE TABLE IF NOT EXISTS scoped_role_policy_modules (
    module_code TEXT PRIMARY KEY,
    module_name TEXT NOT NULL,
    route_scope TEXT NOT NULL,
    current_state TEXT NOT NULL,
    permission_notes TEXT NOT NULL DEFAULT '',
    source_url TEXT NOT NULL DEFAULT '',
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS scoped_role_policy_actions (
    action_code TEXT PRIMARY KEY,
    action_description TEXT NOT NULL,
    is_non_bypassable BOOLEAN NOT NULL DEFAULT FALSE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE IF NOT EXISTS scoped_role_policy_scopes (
    scope_code TEXT PRIMARY KEY,
    scope_description TEXT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE IF NOT EXISTS scoped_role_policy_versions (
    policy_version_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    version_number INTEGER NOT NULL UNIQUE CHECK (version_number > 0),
    policy_name TEXT NOT NULL,
    policy_status TEXT NOT NULL CHECK (policy_status IN ('DRAFT','PUBLISHED','RETIRED')),
    source_name TEXT NOT NULL,
    source_sha256 TEXT NOT NULL,
    policy_notes TEXT NOT NULL DEFAULT '',
    created_by_user_id UUID NULL REFERENCES app_users(user_id),
    published_by_user_id UUID NULL REFERENCES app_users(user_id),
    restored_from_policy_version_id UUID NULL REFERENCES scoped_role_policy_versions(policy_version_id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    published_at TIMESTAMPTZ NULL,
    retired_at TIMESTAMPTZ NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_scoped_role_policy_one_published
ON scoped_role_policy_versions ((policy_status))
WHERE policy_status = 'PUBLISHED';

CREATE TABLE IF NOT EXISTS scoped_role_policy_grants (
    scoped_role_policy_grant_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    policy_version_id UUID NOT NULL REFERENCES scoped_role_policy_versions(policy_version_id) ON DELETE RESTRICT,
    role_code TEXT NOT NULL,
    module_code TEXT NOT NULL REFERENCES scoped_role_policy_modules(module_code),
    action_code TEXT NOT NULL REFERENCES scoped_role_policy_actions(action_code),
    scope_code TEXT NOT NULL REFERENCES scoped_role_policy_scopes(scope_code),
    grant_effect TEXT NOT NULL CHECK (grant_effect IN ('GRANT','DENY')),
    conditions JSONB NOT NULL DEFAULT '{}'::jsonb,
    delegated_authority BOOLEAN NOT NULL DEFAULT FALSE,
    reason_required BOOLEAN NOT NULL DEFAULT FALSE,
    audit_required BOOLEAN NOT NULL DEFAULT TRUE,
    source_designation TEXT NOT NULL,
    source_notes TEXT NOT NULL DEFAULT '',
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (policy_version_id, role_code, module_code, action_code, scope_code, grant_effect)
);

CREATE INDEX IF NOT EXISTS ix_scoped_role_policy_grants_effective
ON scoped_role_policy_grants
(policy_version_id, role_code, module_code, action_code, is_active);

CREATE TABLE IF NOT EXISTS scoped_role_policy_audit_events (
    scoped_role_policy_audit_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    policy_version_id UUID NULL REFERENCES scoped_role_policy_versions(policy_version_id),
    event_code TEXT NOT NULL,
    actor_user_id UUID NULL REFERENCES app_users(user_id),
    actor_email TEXT NOT NULL DEFAULT '',
    reason TEXT NOT NULL DEFAULT '',
    previous_state JSONB NOT NULL DEFAULT '{}'::jsonb,
    new_state JSONB NOT NULL DEFAULT '{}'::jsonb,
    event_metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS scoped_approval_stage_events (
    scoped_approval_stage_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    timesheet_id UUID NOT NULL,
    work_date DATE NOT NULL,
    required_stage TEXT NOT NULL CHECK (required_stage IN ('MANAGER','PROJECT_MANAGER','PTC_FINAL')),
    original_responsible_role TEXT NOT NULL,
    original_responsible_user_id UUID NULL REFERENCES app_users(user_id),
    acting_user_id UUID NOT NULL REFERENCES app_users(user_id),
    acting_role_code TEXT NOT NULL,
    delegated_action BOOLEAN NOT NULL DEFAULT FALSE,
    reason TEXT NOT NULL,
    previous_status TEXT NOT NULL,
    new_status TEXT NOT NULL,
    audit_metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_scoped_approval_stage_events_target
ON scoped_approval_stage_events (timesheet_id, work_date, created_at DESC);

CREATE TABLE IF NOT EXISTS scoped_time_correction_events (
    scoped_time_correction_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    time_entry_id UUID NULL,
    timesheet_id UUID NOT NULL,
    work_date DATE NOT NULL,
    action_code TEXT NOT NULL CHECK (
        action_code IN ('TIME_REOPEN','TIME_CORRECT_ON_BEHALF','TIME_REASSIGN')
    ),
    actor_user_id UUID NOT NULL REFERENCES app_users(user_id),
    target_user_id UUID NOT NULL REFERENCES app_users(user_id),
    reason TEXT NOT NULL,
    original_values JSONB NOT NULL,
    revised_values JSONB NOT NULL,
    previous_status TEXT NOT NULL,
    new_status TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE OR REPLACE FUNCTION projectpulse040_block_immutable_audit_mutation()
RETURNS trigger LANGUAGE plpgsql AS $projectpulse040_immutable_audit$
BEGIN
    RAISE EXCEPTION 'Scoped RBAC audit evidence is immutable.';
END;
$projectpulse040_immutable_audit$;

DROP TRIGGER IF EXISTS trg_projectpulse040_policy_audit_immutable
ON scoped_role_policy_audit_events;
CREATE TRIGGER trg_projectpulse040_policy_audit_immutable
BEFORE UPDATE OR DELETE ON scoped_role_policy_audit_events
FOR EACH ROW EXECUTE FUNCTION projectpulse040_block_immutable_audit_mutation();

DROP TRIGGER IF EXISTS trg_projectpulse040_approval_audit_immutable
ON scoped_approval_stage_events;
CREATE TRIGGER trg_projectpulse040_approval_audit_immutable
BEFORE UPDATE OR DELETE ON scoped_approval_stage_events
FOR EACH ROW EXECUTE FUNCTION projectpulse040_block_immutable_audit_mutation();

DROP TRIGGER IF EXISTS trg_projectpulse040_time_audit_immutable
ON scoped_time_correction_events;
CREATE TRIGGER trg_projectpulse040_time_audit_immutable
BEFORE UPDATE OR DELETE ON scoped_time_correction_events
FOR EACH ROW EXECUTE FUNCTION projectpulse040_block_immutable_audit_mutation();

CREATE OR REPLACE FUNCTION projectpulse040_block_published_grant_mutation()
RETURNS trigger LANGUAGE plpgsql AS $projectpulse040_published_grant$
DECLARE v_policy_status TEXT;
BEGIN
    SELECT policy_status INTO v_policy_status
    FROM scoped_role_policy_versions
    WHERE policy_version_id = COALESCE(OLD.policy_version_id, NEW.policy_version_id);

    IF v_policy_status IN ('PUBLISHED','RETIRED') THEN
        RAISE EXCEPTION
            'Published or retired scoped policy grants are immutable. Publish a new version instead.';
    END IF;

    RETURN COALESCE(NEW, OLD);
END;
$projectpulse040_published_grant$;

DROP TRIGGER IF EXISTS trg_projectpulse040_published_grants_immutable
ON scoped_role_policy_grants;
CREATE TRIGGER trg_projectpulse040_published_grants_immutable
BEFORE UPDATE OR DELETE ON scoped_role_policy_grants
FOR EACH ROW EXECUTE FUNCTION projectpulse040_block_published_grant_mutation();

CREATE OR REPLACE VIEW scoped_role_policy_effective_grants AS
SELECT
    version.policy_version_id,
    version.version_number,
    version.policy_name,
    version.source_name,
    version.source_sha256,
    version.published_at,
    grant_row.scoped_role_policy_grant_id,
    grant_row.role_code,
    grant_row.module_code,
    module_row.module_name,
    module_row.route_scope,
    grant_row.action_code,
    grant_row.scope_code,
    grant_row.grant_effect,
    grant_row.conditions,
    grant_row.delegated_authority,
    grant_row.reason_required,
    grant_row.audit_required,
    grant_row.source_designation,
    grant_row.source_notes,
    grant_row.is_active
FROM scoped_role_policy_versions version
JOIN scoped_role_policy_grants grant_row
  ON grant_row.policy_version_id = version.policy_version_id
JOIN scoped_role_policy_modules module_row
  ON module_row.module_code = grant_row.module_code
WHERE version.policy_status = 'PUBLISHED'
  AND grant_row.is_active = TRUE;

INSERT INTO scoped_role_policy_modules (
    module_code, module_name, route_scope, current_state,
    permission_notes, source_url, is_active
)
SELECT
    module_row->>0,
    module_row->>1,
    module_row->>2,
    module_row->>3,
    '',
    'ProjectPulse_Module_Role_Permissions_Matrix(2).xlsx',
    TRUE
FROM jsonb_array_elements('[
["001","Time Entry","timesheet","Installed"],
["002","Approval Inbox","manager-approval","Installed"],
["003","Utilization","utilization","Installed"],
["004","Holiday Calendar","holiday-admin","Installed"],
["005","Project Allocation and Info","project-allocation-info","Installed legacy behavior"],
["006","PSA Modules","psa-modules","Installed legacy behavior"],
["007","Workflow","workflow","Installed"],
["008","Audit / Security History","audit-history","Installed"],
["009","User Administration","user-admin","Installed"],
["010","Azure / Entra Admin","azure-admin","Installed"],
["011","Work Task Builder","work-task-builder","Installed"],
["012","Role Administration","role-admin","Installed"],
["013","Service Control Center","service-control","Installed"],
["014","Backup / DR Center","backup-dr","Installed"],
["015","Restore Validation","restore-validation","Installed"],
["016","Backup Retention","backup-retention","Installed"],
["017","Replication & Sync Status","replication-sync","Installed"],
["018","Project Workload","project-workload","Installed"],
["019","Project Workspace & Engineering Documents","project-workspace","Installed"],
["020","Project Intake & Engineering Resource Requests","project-intake","Installed"],
["021","Customer Directory","customer-directory","Installed"],
["022","Cost Overrun Alerts","cost-alerts","Installed"],
["023","Time Compliance & Notification Center","time-compliance","Installed"],
["024","Sales-to-Delivery Intake Foundation","sales-intake","Installed"],
["025","SOW Generator + Claude Review Workflow","sow-generator","Installed"],
["026","CRM/ERP Integration Control Center","crm-integration","Source implemented"],
["027","Signed SOW Handoff + Assignment Trigger","signed-handoff","Installed"],
["028","SOW-Aware AI Time Entry Generator","ai-time-entry","Installed"],
["029","User Acceptance / Role + Workflow Validation Center","uat-validation","Installed"],
["030","Reporting / Accounting / Invoicing / Analytics","reporting","Installed"],
["034","Dashboard and Navigation Labeling","Global enhancement","Installed"],
["035","Guided Project Intake Launch","Embedded in Module 020","Installed"],
["036","Sales Insights Dashboard","sales-insights","Installed"],
["037","Roles and Permissions Matrix","roles-permissions-matrix","Installed"],
["038","Certify Integration Center","certify-integration","Installed"],
["039","Billing Readiness Center","billing-readiness","Installed"],
["040","Project Closeout Center","project-closeout","Installed"],
["041","Closeout Email Automation Center","closeout-email","Installed"],
["042","Invoice & Billing Center","invoice-billing-center","Installed route"],
["055B","Rate Card Administration","rate-card-administration","Installed"],
["055C","Manage Existing Projects","work-register","Deployed to test"],
["055D","Create New Project","create-work-register","Deployed to test"],
["056E","Contract-Management Evolution Guard","Cross-cutting invariant","Protected"],
["057","Resource & Team Calendar Capacity","calendar-capacity","Installed"],
["058","Autonomous CI/CD Foundation","cicd-pipeline","Installed"],
["059","Global Session Intelligence","All authenticated routes","Installed global behavior"],
["060","Contracts & Block of Hours","contracts","Installed"],
["061","Undefined / Reserved","No route","Scope required"],
["062","Unified Identity Profile and Presence","Profile and identity APIs","Installed source"],
["063","Opportunities & Action Tracker","opportunities","Installed"],
["064","AI Provider Configuration Center","ai-provider-configuration","Installed"],
["065","Entra Secret Administration","entra-secret-administration","Installed fail-closed"],
["066","Project FlowHive","project-flowhive","Installed safe source"],
["067","Global Mail Configuration Center","global-mail-configuration","Installed read-only"],
["068","System Architecture & Dependency Map","system-architecture","Installed read-only"],
["069","Qualifications & Certification Matrix","qualifications-certifications","Installed read-only"],
["070","Capacity & Pipeline Forecasting","capacity-pipeline-forecast","Installed"],
["071","On-Call Scheduling","on-call-scheduling","Deployed to test"],
["072","OneAssist Routing PIN Directory","oneassist-routing-directory","Deployed to test"],
["073","Sales Coverage Alignment","sales-coverage-alignment","Installed draft source"],
["074","OEM & Vendor Directory","oem-vendor-directory","Installed draft source"],
["075","Integration Automation & Event Gateway","Source-integrated scope","Source integrated"],
["076","Defect Intake & Resolution Tracker","defect-tracker","Installed fail-closed"],
["077","Release, Deployment & Rollback Control Center","Source-integrated scope","Source integrated"],
["078","Observability, SLO & Application Health Center","Source-integrated scope","Source integrated"],
["079","Data Governance, Retention & Privacy Center","Source-integrated scope","Source integrated"],
["080","Customer Delivery & Acceptance Portal","Source-integrated scope","Source integrated"],
["997","Security Operations, Threat Intelligence & Response Center","security-operations","Test accepted; production pending"],
["998","System Diagnostic & Controlled Remediation Center","system-diagnostic-remediation","Test accepted; production pending"],
["999","ProjectPulse Complete User Guide","user-guide","Installed"]
]'::jsonb) AS module_row
ON CONFLICT (module_code) DO UPDATE
SET module_name = EXCLUDED.module_name,
    route_scope = EXCLUDED.route_scope,
    current_state = EXCLUDED.current_state,
    is_active = TRUE;

INSERT INTO scoped_role_policy_actions (
    action_code, action_description, is_non_bypassable, is_active
)
SELECT
    action_code,
    initcap(lower(replace(action_code, '_', ' '))),
    action_code = ANY(ARRAY(
        SELECT jsonb_array_elements_text('["APPROVAL_DELETE_PERMANENT","APPROVAL_HISTORY_EDIT","APPROVAL_SYSTEM_CONFIGURE","AUDIT_BYPASS","NON_BYPASSABLE_SAFETY_BYPASS","SYSTEM_CONFIGURE","TIME_DELETE_PERMANENT","USER_IMPERSONATE","UTILIZATION_EDIT"]'::jsonb)
    )),
    TRUE
FROM jsonb_array_elements_text('["ACCESS_EXPLAIN","APPROVAL_APPROVE","APPROVAL_APPROVE_MANAGER","APPROVAL_APPROVE_PROJECT_MANAGER","APPROVAL_APPROVE_PTC_FINAL","APPROVAL_DELEGATE_MANAGER","APPROVAL_DELEGATE_PROJECT_MANAGER","APPROVAL_DELETE_PERMANENT","APPROVAL_HISTORY_EDIT","APPROVAL_REJECT","APPROVAL_REJECT_MANAGER","APPROVAL_REJECT_PROJECT_MANAGER","APPROVAL_REJECT_PTC_FINAL","APPROVAL_RETURN_FOR_CORRECTION","APPROVAL_SYSTEM_CONFIGURE","APPROVAL_VIEW","APPROVAL_VIEW_MANAGER","APPROVAL_VIEW_PROJECT_MANAGER","APPROVAL_VIEW_PTC_FINAL","AUDIT_BYPASS","AUDIT_RECORD","AUDIT_VIEW","DELEGATED_ACTION","EXPORT_DATA","MATRIX_EXPORT","MATRIX_VIEW","MODULE_ACCESS","MODULE_CONFIGURE","MODULE_VIEW","NON_BYPASSABLE_SAFETY_BYPASS","PASSWORD_RESET_APPROVE","POLICY_AUDIT_VIEW","POLICY_DELEGATE","POLICY_PUBLISH","POLICY_RESTORE","POLICY_VALIDATE","POLICY_VIEW","RECORD_ASSIGN","RECORD_CREATE","RECORD_EDIT","RECORD_REOPEN","SYSTEM_CONFIGURE","TIME_APPROVE","TIME_CORRECT_ON_BEHALF","TIME_DELETE_PERMANENT","TIME_EDIT_OWN","TIME_REASSIGN","TIME_REJECT","TIME_REOPEN","TIME_SUBMIT","TIME_VIEW","USER_IMPERSONATE","UTILIZATION_EDIT","UTILIZATION_VIEW","WORKFLOW_MANAGE"]'::jsonb) AS action_code
ON CONFLICT (action_code) DO UPDATE
SET is_non_bypassable = EXCLUDED.is_non_bypassable,
    is_active = TRUE;

INSERT INTO scoped_role_policy_scopes (
    scope_code, scope_description, is_active
)
SELECT key, value, TRUE
FROM jsonb_each_text('{"SELF":"The effective user only.","MANAGED_TEAM":"Direct reports managed by the effective user.","FUNCTIONAL_TEAM":"Users in the same authorized functional team or department.","DIRECT_AND_INDIRECT_REPORTS":"Direct and indirect reports in the management hierarchy.","ASSIGNED_PROJECTS":"Projects assigned to the effective user.","ASSIGNED_PROJECT_TEAM":"Users or work items on projects assigned to the effective user.","MANAGED_PROJECTS":"Projects managed by the effective user.","ASSIGNED_CUSTOMERS":"Customers assigned to the effective user.","ORGANIZATION":"Organization-wide scope, subject to non-bypassable controls.","CUSTOM_RULE":"A fail-closed condition-driven scope."}'::jsonb)
ON CONFLICT (scope_code) DO UPDATE
SET scope_description = EXCLUDED.scope_description,
    is_active = TRUE;

INSERT INTO scoped_role_policy_versions (
    policy_version_id, version_number, policy_name, policy_status,
    source_name, source_sha256, policy_notes,
    created_by_user_id, published_by_user_id, created_at, published_at
)
VALUES (
    '04000000-0000-0000-0000-000000000001'::uuid,
    1,
    'ProjectPulse Workbook Baseline',
    'PUBLISHED',
    'ProjectPulse_Module_Role_Permissions_Matrix(2).xlsx',
    'a9d8d1549ad36634d0a84510326e2127e644c3d14a4be2877fb659ef4a56c02c',
    'Approved workbook baseline. Not Set cells intentionally preserve legacy behavior.',
    (
        SELECT u.user_id
        FROM app_users u
        JOIN app_user_role_assignments ura
          ON ura.user_id = u.user_id AND ura.is_active = TRUE
        JOIN app_roles r
          ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
        WHERE upper(r.role_code) IN ('SUPER_ADMINISTRATOR','ADMINISTRATOR')
          AND u.is_active = TRUE
        ORDER BY u.created_at, u.user_id
        LIMIT 1
    ),
    (
        SELECT u.user_id
        FROM app_users u
        JOIN app_user_role_assignments ura
          ON ura.user_id = u.user_id AND ura.is_active = TRUE
        JOIN app_roles r
          ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
        WHERE upper(r.role_code) IN ('SUPER_ADMINISTRATOR','ADMINISTRATOR')
          AND u.is_active = TRUE
        ORDER BY u.created_at, u.user_id
        LIMIT 1
    ),
    NOW(),
    NOW()
)
ON CONFLICT (policy_version_id) DO NOTHING;
