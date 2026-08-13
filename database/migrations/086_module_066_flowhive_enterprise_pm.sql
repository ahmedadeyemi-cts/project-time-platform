-- ProjectPulse migration 086
-- Module 066 Project FlowHive enterprise project-management workspace.
--
-- This migration adds PM-owned working copies, project controls, RAID logs,
-- governed status reports, and reviewed customer-share tokens. It also aligns
-- approved SOW/GSD document metadata with the private retrieval contract used
-- by FlowHive. It does not expose raw document text, provider credentials, or
-- unrestricted customer links.

BEGIN;

DO $projectpulse086_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.projects') IS NULL
       OR to_regclass('public.app_users') IS NULL
       OR to_regclass('public.project_flowhive_plans') IS NULL
       OR to_regclass('public.project_flowhive_plan_versions') IS NULL
       OR to_regclass('public.project_flowhive_audit_events') IS NULL
       OR to_regclass('public.project_intake_documents') IS NULL THEN
        RAISE EXCEPTION 'Migration 086 requires migrations 074, 079, and 085 plus canonical project and identity foundations.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id = '085_module_019_document_access_storage_repair'
    ) THEN
        RAISE EXCEPTION 'Migration 086 requires migration 085.';
    END IF;
END;
$projectpulse086_prerequisites$;

CREATE TABLE IF NOT EXISTS project_flowhive_working_copies (
    project_id UUID PRIMARY KEY REFERENCES projects(project_id) ON DELETE CASCADE,
    plan_id UUID NULL REFERENCES project_flowhive_plans(plan_id) ON DELETE SET NULL,
    working_payload JSONB NOT NULL DEFAULT '{}'::JSONB
        CHECK (jsonb_typeof(working_payload) = 'object'),
    working_revision INTEGER NOT NULL DEFAULT 1 CHECK (working_revision >= 1),
    row_version UUID NOT NULL DEFAULT gen_random_uuid(),
    updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_project_flowhive_working_copies_updated
    ON project_flowhive_working_copies(updated_at DESC);

CREATE TABLE IF NOT EXISTS project_flowhive_project_controls (
    project_id UUID PRIMARY KEY REFERENCES projects(project_id) ON DELETE CASCADE,
    contract_type VARCHAR(40) NOT NULL DEFAULT 'unknown' CHECK (contract_type IN (
        'fixed_price', 'time_and_materials', 'hybrid', 'internal', 'not_billable', 'unknown'
    )),
    currency_code CHAR(3) NOT NULL DEFAULT 'USD'
        CHECK (currency_code ~ '^[A-Z]{3}$'),
    approved_budget NUMERIC(18,2) NULL CHECK (approved_budget IS NULL OR approved_budget >= 0),
    expense_budget NUMERIC(18,2) NULL CHECK (expense_budget IS NULL OR expense_budget >= 0),
    contingency_budget NUMERIC(18,2) NULL CHECK (contingency_budget IS NULL OR contingency_budget >= 0),
    forecast_at_completion NUMERIC(18,2) NULL CHECK (forecast_at_completion IS NULL OR forecast_at_completion >= 0),
    percent_complete_method VARCHAR(32) NOT NULL DEFAULT 'task_weighted' CHECK (percent_complete_method IN (
        'task_weighted', 'effort_weighted', 'manual', 'earned_value'
    )),
    status_report_cadence VARCHAR(32) NOT NULL DEFAULT 'weekly' CHECK (status_report_cadence IN (
        'weekly', 'biweekly', 'monthly', 'milestone', 'manual'
    )),
    customer_sharing_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    financial_notes TEXT NOT NULL DEFAULT '',
    updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS project_flowhive_raid_items (
    raid_item_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    plan_id UUID NULL REFERENCES project_flowhive_plans(plan_id) ON DELETE SET NULL,
    item_type VARCHAR(24) NOT NULL CHECK (item_type IN (
        'risk', 'issue', 'action', 'decision', 'assumption', 'dependency', 'change'
    )),
    title VARCHAR(240) NOT NULL CHECK (length(btrim(title)) >= 3),
    description TEXT NOT NULL DEFAULT '',
    status VARCHAR(32) NOT NULL DEFAULT 'open' CHECK (status IN (
        'open', 'monitoring', 'blocked', 'in_progress', 'accepted', 'mitigated',
        'resolved', 'closed', 'deferred', 'rejected'
    )),
    priority VARCHAR(16) NOT NULL DEFAULT 'medium' CHECK (priority IN (
        'low', 'medium', 'high', 'critical'
    )),
    probability SMALLINT NULL CHECK (probability IS NULL OR probability BETWEEN 1 AND 5),
    impact SMALLINT NULL CHECK (impact IS NULL OR impact BETWEEN 1 AND 5),
    owner_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    due_date DATE NULL,
    mitigation TEXT NOT NULL DEFAULT '',
    source_kind VARCHAR(32) NOT NULL DEFAULT 'manual' CHECK (source_kind IN (
        'manual', 'celar_ai', 'plan', 'financial', 'customer', 'engineering'
    )),
    source_reference VARCHAR(240) NOT NULL DEFAULT '',
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_project_flowhive_raid_project_status
    ON project_flowhive_raid_items(project_id, status, priority, due_date);
CREATE INDEX IF NOT EXISTS ix_project_flowhive_raid_owner
    ON project_flowhive_raid_items(owner_user_id, status, due_date);

CREATE TABLE IF NOT EXISTS project_flowhive_status_reports (
    status_report_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    plan_id UUID NULL REFERENCES project_flowhive_plans(plan_id) ON DELETE SET NULL,
    plan_version_number INTEGER NULL CHECK (plan_version_number IS NULL OR plan_version_number >= 1),
    status_date DATE NOT NULL DEFAULT CURRENT_DATE,
    period_start DATE NULL,
    period_end DATE NULL,
    overall_health VARCHAR(16) NOT NULL DEFAULT 'green' CHECK (overall_health IN (
        'green', 'amber', 'red', 'complete', 'not_started'
    )),
    schedule_health VARCHAR(16) NOT NULL DEFAULT 'green' CHECK (schedule_health IN (
        'green', 'amber', 'red', 'unknown'
    )),
    financial_health VARCHAR(16) NOT NULL DEFAULT 'green' CHECK (financial_health IN (
        'green', 'amber', 'red', 'unknown'
    )),
    scope_health VARCHAR(16) NOT NULL DEFAULT 'green' CHECK (scope_health IN (
        'green', 'amber', 'red', 'unknown'
    )),
    executive_summary TEXT NOT NULL,
    accomplishments JSONB NOT NULL DEFAULT '[]'::JSONB CHECK (jsonb_typeof(accomplishments) = 'array'),
    next_steps JSONB NOT NULL DEFAULT '[]'::JSONB CHECK (jsonb_typeof(next_steps) = 'array'),
    decisions_needed JSONB NOT NULL DEFAULT '[]'::JSONB CHECK (jsonb_typeof(decisions_needed) = 'array'),
    key_risks JSONB NOT NULL DEFAULT '[]'::JSONB CHECK (jsonb_typeof(key_risks) = 'array'),
    financial_snapshot JSONB NOT NULL DEFAULT '{}'::JSONB CHECK (jsonb_typeof(financial_snapshot) = 'object'),
    schedule_snapshot JSONB NOT NULL DEFAULT '{}'::JSONB CHECK (jsonb_typeof(schedule_snapshot) = 'object'),
    generated_source VARCHAR(32) NOT NULL DEFAULT 'deterministic' CHECK (generated_source IN (
        'deterministic', 'celar_ai', 'pm_edited'
    )),
    celar_ai_correlation_id VARCHAR(180) NOT NULL DEFAULT '',
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_project_flowhive_status_project_date
    ON project_flowhive_status_reports(project_id, status_date DESC, created_at DESC);

CREATE TABLE IF NOT EXISTS project_flowhive_customer_shares (
    share_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    plan_id UUID NOT NULL REFERENCES project_flowhive_plans(plan_id) ON DELETE RESTRICT,
    version_number INTEGER NOT NULL CHECK (version_number >= 1),
    token_sha256 CHAR(64) NOT NULL UNIQUE CHECK (token_sha256 ~ '^[0-9a-f]{64}$'),
    customer_label VARCHAR(240) NOT NULL DEFAULT '',
    share_note TEXT NOT NULL DEFAULT '',
    allowed_artifacts TEXT[] NOT NULL DEFAULT ARRAY['view','pdf']::TEXT[],
    expires_at TIMESTAMPTZ NOT NULL,
    revoked_at TIMESTAMPTZ NULL,
    revoked_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    revocation_reason VARCHAR(500) NOT NULL DEFAULT '',
    last_accessed_at TIMESTAMPTZ NULL,
    access_count INTEGER NOT NULL DEFAULT 0 CHECK (access_count >= 0),
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CHECK (expires_at > created_at),
    CHECK (revoked_at IS NULL OR revoked_at >= created_at)
);

CREATE INDEX IF NOT EXISTS ix_project_flowhive_shares_project
    ON project_flowhive_customer_shares(project_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_project_flowhive_shares_active
    ON project_flowhive_customer_shares(expires_at)
    WHERE revoked_at IS NULL;

CREATE TABLE IF NOT EXISTS project_flowhive_share_access_events (
    share_access_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    share_id UUID NOT NULL REFERENCES project_flowhive_customer_shares(share_id) ON DELETE CASCADE,
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    event_code VARCHAR(40) NOT NULL CHECK (event_code IN (
        'viewed', 'pdf_downloaded', 'expired', 'revoked', 'invalid_token', 'blocked'
    )),
    client_fingerprint_sha256 CHAR(64) NOT NULL DEFAULT '',
    user_agent_sha256 CHAR(64) NOT NULL DEFAULT '',
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_project_flowhive_share_access
    ON project_flowhive_share_access_events(share_id, occurred_at DESC);

CREATE OR REPLACE FUNCTION projectpulse086_touch_working_copy()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse086_touch_working_copy_body$
BEGIN
    NEW.updated_at := NOW();
    NEW.working_revision := OLD.working_revision + 1;
    NEW.row_version := gen_random_uuid();
    RETURN NEW;
END;
$projectpulse086_touch_working_copy_body$;

DROP TRIGGER IF EXISTS trg_project_flowhive_working_copy_touch_086
    ON project_flowhive_working_copies;
CREATE TRIGGER trg_project_flowhive_working_copy_touch_086
BEFORE UPDATE ON project_flowhive_working_copies
FOR EACH ROW EXECUTE FUNCTION projectpulse086_touch_working_copy();

CREATE OR REPLACE FUNCTION projectpulse086_touch_controls()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse086_touch_controls_body$
BEGIN
    NEW.updated_at := NOW();
    RETURN NEW;
END;
$projectpulse086_touch_controls_body$;

DROP TRIGGER IF EXISTS trg_project_flowhive_controls_touch_086
    ON project_flowhive_project_controls;
CREATE TRIGGER trg_project_flowhive_controls_touch_086
BEFORE UPDATE ON project_flowhive_project_controls
FOR EACH ROW EXECUTE FUNCTION projectpulse086_touch_controls();

DROP TRIGGER IF EXISTS trg_project_flowhive_raid_touch_086
    ON project_flowhive_raid_items;
CREATE TRIGGER trg_project_flowhive_raid_touch_086
BEFORE UPDATE ON project_flowhive_raid_items
FOR EACH ROW EXECUTE FUNCTION projectpulse086_touch_controls();

CREATE OR REPLACE FUNCTION projectpulse086_immutable_status_report()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse086_immutable_status_report_body$
BEGIN
    RAISE EXCEPTION 'Project FlowHive status-report evidence is immutable. Create a new status report instead.';
END;
$projectpulse086_immutable_status_report_body$;

DROP TRIGGER IF EXISTS trg_project_flowhive_status_report_immutable_086
    ON project_flowhive_status_reports;
CREATE TRIGGER trg_project_flowhive_status_report_immutable_086
BEFORE UPDATE OR DELETE ON project_flowhive_status_reports
FOR EACH ROW EXECUTE FUNCTION projectpulse086_immutable_status_report();

CREATE OR REPLACE FUNCTION projectpulse086_classify_flowhive_document()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse086_classify_flowhive_document_body$
DECLARE
    candidate TEXT;
BEGIN
    candidate := lower(COALESCE(NEW.original_file_name, '') || ' ' ||
                       COALESCE(NEW.document_type, '') || ' ' ||
                       COALESCE(NEW.document_category, ''));

    IF candidate ~ '(^|[^a-z])(statement[ _-]*of[ _-]*work|sow)([^a-z]|$)' THEN
        IF lower(COALESCE(NEW.document_category, '')) IN ('', 'other', 'supporting', 'project_document') THEN
            NEW.document_category := 'sow';
        END IF;
        NEW.engineering_visible := TRUE;
    ELSIF candidate ~ '(^|[^a-z])(global[ _-]*solution[ _-]*design|gsd)([^a-z]|$)' THEN
        IF lower(COALESCE(NEW.document_category, '')) IN ('', 'other', 'supporting', 'project_document') THEN
            NEW.document_category := 'gsd';
        END IF;
        NEW.engineering_visible := TRUE;
    END IF;

    RETURN NEW;
END;
$projectpulse086_classify_flowhive_document_body$;

DROP TRIGGER IF EXISTS trg_projectpulse086_classify_flowhive_document
    ON project_intake_documents;
CREATE TRIGGER trg_projectpulse086_classify_flowhive_document
BEFORE INSERT OR UPDATE OF original_file_name, document_type, document_category
ON project_intake_documents
FOR EACH ROW EXECUTE FUNCTION projectpulse086_classify_flowhive_document();

-- Normalize existing SOW/GSD metadata without treating an unknown document as
-- evidence. Readiness still requires private processing, an approved/canonical
-- active version, and citation-ready chunks.
UPDATE project_intake_documents
SET document_category = CASE
        WHEN lower(COALESCE(original_file_name, '') || ' ' || COALESCE(document_type, '') || ' ' || COALESCE(document_category, ''))
             ~ '(^|[^a-z])(statement[ _-]*of[ _-]*work|sow)([^a-z]|$)' THEN 'sow'
        WHEN lower(COALESCE(original_file_name, '') || ' ' || COALESCE(document_type, '') || ' ' || COALESCE(document_category, ''))
             ~ '(^|[^a-z])(global[ _-]*solution[ _-]*design|gsd)([^a-z]|$)' THEN 'gsd'
        ELSE document_category
    END,
    engineering_visible = TRUE
WHERE is_active = TRUE
  AND (
      lower(COALESCE(original_file_name, '') || ' ' || COALESCE(document_type, '') || ' ' || COALESCE(document_category, ''))
        ~ '(^|[^a-z])(statement[ _-]*of[ _-]*work|sow)([^a-z]|$)'
      OR
      lower(COALESCE(original_file_name, '') || ' ' || COALESCE(document_type, '') || ' ' || COALESCE(document_category, ''))
        ~ '(^|[^a-z])(global[ _-]*solution[ _-]*design|gsd)([^a-z]|$)'
  );

INSERT INTO app_permissions(
    permission_code, permission_name, module_code, permission_description)
VALUES
    ('MANAGE_FLOWHIVE_PM_WORKSPACE_066', 'Manage FlowHive PM workspace', '066', 'Manage enterprise FlowHive working copies, project controls, RAID items, status reports, and reviewed artifacts for projects owned by the current Project Manager.'),
    ('CREATE_FLOWHIVE_CUSTOMER_SHARE_066', 'Create FlowHive customer share', '066', 'Create expiring and revocable customer views from an exact reviewed FlowHive baseline for projects owned by the current Project Manager.'),
    ('VIEW_FLOWHIVE_FINANCIALS_066', 'View FlowHive project financials', '066', 'View governed Module 055C/005/financial truth in FlowHive within authorized project scope.')
ON CONFLICT(permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

WITH desired(role_code, permission_code) AS (
    VALUES
        ('SUPER_ADMINISTRATOR', 'MANAGE_FLOWHIVE_PM_WORKSPACE_066'),
        ('SUPER_ADMINISTRATOR', 'CREATE_FLOWHIVE_CUSTOMER_SHARE_066'),
        ('SUPER_ADMINISTRATOR', 'VIEW_FLOWHIVE_FINANCIALS_066'),
        ('SYSTEM_ADMINISTRATOR', 'MANAGE_FLOWHIVE_PM_WORKSPACE_066'),
        ('SYSTEM_ADMINISTRATOR', 'CREATE_FLOWHIVE_CUSTOMER_SHARE_066'),
        ('SYSTEM_ADMINISTRATOR', 'VIEW_FLOWHIVE_FINANCIALS_066'),
        ('ADMINISTRATOR', 'MANAGE_FLOWHIVE_PM_WORKSPACE_066'),
        ('ADMINISTRATOR', 'CREATE_FLOWHIVE_CUSTOMER_SHARE_066'),
        ('ADMINISTRATOR', 'VIEW_FLOWHIVE_FINANCIALS_066'),
        ('PROJECT_MANAGER', 'MANAGE_FLOWHIVE_PM_WORKSPACE_066'),
        ('PROJECT_MANAGER', 'CREATE_FLOWHIVE_CUSTOMER_SHARE_066'),
        ('PROJECT_MANAGER', 'VIEW_FLOWHIVE_FINANCIALS_066'),
        ('PROJECT_MANAGEMENT', 'MANAGE_FLOWHIVE_PM_WORKSPACE_066'),
        ('PROJECT_MANAGEMENT', 'CREATE_FLOWHIVE_CUSTOMER_SHARE_066'),
        ('PROJECT_MANAGEMENT', 'VIEW_FLOWHIVE_FINANCIALS_066'),
        ('PROJECT_MANAGEMENT_LEAD', 'VIEW_FLOWHIVE_FINANCIALS_066'),
        ('PROJECT_MANAGEMENT_TEAM_LEAD', 'VIEW_FLOWHIVE_FINANCIALS_066'),
        ('PM_TEAM_LEAD', 'VIEW_FLOWHIVE_FINANCIALS_066'),
        ('PROJECT_TEAM_COORDINATOR', 'VIEW_FLOWHIVE_FINANCIALS_066'),
        ('EXECUTIVE', 'VIEW_FLOWHIVE_FINANCIALS_066')
), candidates AS (
    SELECT role.app_role_id, permission.app_permission_id
    FROM desired
    JOIN app_roles role ON upper(role.role_code) = desired.role_code AND role.is_active = TRUE
    JOIN app_permissions permission ON permission.permission_code = desired.permission_code
)
INSERT INTO app_role_permissions(app_role_id, app_permission_id, created_at)
SELECT app_role_id, app_permission_id, NOW()
FROM candidates
ON CONFLICT(app_role_id, app_permission_id) DO NOTHING;

INSERT INTO schema_migrations(migration_id, description, applied_at)
VALUES(
    '086_module_066_flowhive_enterprise_pm',
    'Enable PM-owned FlowHive working copies, enterprise controls, RAID and status reporting, governed customer shares, and SOW/GSD evidence metadata alignment',
    NOW())
ON CONFLICT(migration_id) DO NOTHING;

COMMIT;
