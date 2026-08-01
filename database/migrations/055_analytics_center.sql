-- Module 030 Analytics Center
-- Dynamic role-scoped analytics, immutable runs/exports, personal saved views,
-- and compatibility permissions for the existing reporting foundation.

BEGIN;

CREATE TABLE IF NOT EXISTS enterprise_report_runs (
    enterprise_report_run_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    report_code VARCHAR(120) NOT NULL,
    report_name VARCHAR(220) NOT NULL,
    actual_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    effective_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    scope_snapshot_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    filters_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    columns_json JSONB NOT NULL DEFAULT '[]'::JSONB,
    result_status VARCHAR(40) NOT NULL CHECK (result_status IN (
        'complete', 'partial', 'no_data', 'source_unavailable', 'failed'
    )),
    row_count INTEGER NOT NULL DEFAULT 0 CHECK (row_count >= 0),
    source_states_json JSONB NOT NULL DEFAULT '[]'::JSONB,
    result_json JSONB NOT NULL DEFAULT '[]'::JSONB,
    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_enterprise_report_runs_effective_user
    ON enterprise_report_runs(effective_user_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_enterprise_report_runs_report
    ON enterprise_report_runs(report_code, created_at DESC);

CREATE TABLE IF NOT EXISTS enterprise_report_saved_views (
    enterprise_report_saved_view_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    view_name VARCHAR(160) NOT NULL,
    report_code VARCHAR(120) NOT NULL,
    owner_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    filters_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    is_default BOOLEAN NOT NULL DEFAULT FALSE,
    version INTEGER NOT NULL DEFAULT 1 CHECK (version > 0),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_enterprise_report_saved_views_owner_name
    ON enterprise_report_saved_views(owner_user_id, lower(view_name));
CREATE UNIQUE INDEX IF NOT EXISTS ux_enterprise_report_saved_views_default
    ON enterprise_report_saved_views(owner_user_id)
    WHERE is_default = TRUE;

CREATE TABLE IF NOT EXISTS enterprise_report_exports (
    enterprise_report_export_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    enterprise_report_run_id UUID NOT NULL REFERENCES enterprise_report_runs(enterprise_report_run_id) ON DELETE RESTRICT,
    actor_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    export_format VARCHAR(20) NOT NULL CHECK (export_format IN ('csv', 'xlsx', 'json')),
    row_count INTEGER NOT NULL DEFAULT 0 CHECK (row_count >= 0),
    content_sha256 VARCHAR(64) NOT NULL CHECK (content_sha256 ~ '^[0-9a-f]{64}$'),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_enterprise_report_exports_run
    ON enterprise_report_exports(enterprise_report_run_id, created_at DESC);

CREATE OR REPLACE FUNCTION projectpulse055_block_analytics_evidence_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse055_analytics_immutable$
BEGIN
    RAISE EXCEPTION 'Analytics Center run and export evidence is immutable.';
END;
$projectpulse055_analytics_immutable$;

DROP TRIGGER IF EXISTS trg_projectpulse055_analytics_runs_immutable ON enterprise_report_runs;
CREATE TRIGGER trg_projectpulse055_analytics_runs_immutable
BEFORE UPDATE OR DELETE ON enterprise_report_runs
FOR EACH ROW EXECUTE FUNCTION projectpulse055_block_analytics_evidence_mutation();

DROP TRIGGER IF EXISTS trg_projectpulse055_analytics_exports_immutable ON enterprise_report_exports;
CREATE TRIGGER trg_projectpulse055_analytics_exports_immutable
BEFORE UPDATE OR DELETE ON enterprise_report_exports
FOR EACH ROW EXECUTE FUNCTION projectpulse055_block_analytics_evidence_mutation();

INSERT INTO app_permissions (
    permission_code,
    permission_name,
    module_code,
    permission_description
)
VALUES
    ('VIEW_ENTERPRISE_REPORTING', 'View Analytics Center', '030', 'View the role-scoped Analytics Center catalog, populated criteria, previews, sources, and run history.'),
    ('RUN_ENTERPRISE_REPORTING', 'Run Analytics', '030', 'Run role-scoped Analytics Center reports and record immutable execution evidence.'),
    ('EXPORT_ENTERPRISE_REPORTING', 'Export Analytics', '030', 'Export an authorized persisted Analytics Center run to XLSX, CSV, or JSON with immutable checksum evidence.'),
    ('MANAGE_ENTERPRISE_REPORTING', 'Manage Analytics Center', '030', 'Administer governed Analytics Center capabilities without expanding customer, project, person, or financial-field scope.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

UPDATE app_feature_catalog
SET is_active = FALSE,
    updated_at = NOW()
WHERE feature_code = 'FINANCIAL_REPORT_CENTER';

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
VALUES (
    'ANALYTICS_CENTER',
    'Analytics Center',
    '030',
    '#reporting',
    'VIEW_ENTERPRISE_REPORTING',
    'Intuitive role-scoped analytics across customers, projects, financials, time, Engineers, Project Managers, teams, delivery, operations, governance, and acceptance.',
    300,
    TRUE
)
ON CONFLICT (feature_code) DO UPDATE
SET feature_name = EXCLUDED.feature_name,
    module_code = EXCLUDED.module_code,
    route_anchor = EXCLUDED.route_anchor,
    required_permission_code = EXCLUDED.required_permission_code,
    feature_description = EXCLUDED.feature_description,
    display_order = EXCLUDED.display_order,
    is_active = TRUE,
    updated_at = NOW();

-- Organization reporting administrators receive full Analytics Center capabilities.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
CROSS JOIN app_permissions permission
WHERE upper(role.role_code) IN (
    'SUPER_ADMINISTRATOR', 'ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR',
    'ACCOUNTING', 'ACCOUNTING_BILLING', 'BILLING', 'FINANCE',
    'EXECUTIVE', 'EXECUTIVE_LEADERSHIP'
)
AND permission.permission_code IN (
    'VIEW_ENTERPRISE_REPORTING', 'RUN_ENTERPRISE_REPORTING',
    'EXPORT_ENTERPRISE_REPORTING', 'MANAGE_ENTERPRISE_REPORTING'
)
ON CONFLICT DO NOTHING;

-- Delivery and people roles can use only the server-authorized records and fields
-- already available to their effective identity.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
CROSS JOIN app_permissions permission
WHERE upper(role.role_code) IN (
    'PROJECT_MANAGER', 'PROJECT_MANAGEMENT', 'PROJECT_MANAGEMENT_LEAD',
    'PROJECT_MANAGEMENT_TEAM_LEAD', 'PM_TEAM_LEAD',
    'ENGINEER', 'ENGINEERING', 'ENGINEERING_LEAD', 'ENGINEERING_TEAM_LEAD',
    'ENGINEERING_MANAGER', 'MANAGER', 'SOLUTION_ARCHITECT',
    'SALES', 'INSIDE_SALES', 'ACCOUNT_EXECUTIVE'
)
AND permission.permission_code IN (
    'VIEW_ENTERPRISE_REPORTING', 'RUN_ENTERPRISE_REPORTING', 'EXPORT_ENTERPRISE_REPORTING'
)
ON CONFLICT DO NOTHING;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '055_analytics_center',
    'Module 030 Analytics Center immutable runs, exports, saved views, populated dynamic filters, and role-scoped permissions',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
