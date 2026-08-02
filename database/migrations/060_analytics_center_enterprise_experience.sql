-- Module 030 Analytics Center enterprise experience
-- Recurring schedules, per-recipient governed delivery, recent/favorite activity,
-- branded PDF export support, and immutable schedule/delivery evidence.

BEGIN;

ALTER TABLE enterprise_report_exports
    DROP CONSTRAINT IF EXISTS enterprise_report_exports_export_format_check;
ALTER TABLE enterprise_report_exports
    ADD CONSTRAINT enterprise_report_exports_export_format_check
    CHECK (export_format IN ('csv', 'xlsx', 'json', 'pdf'));

CREATE TABLE IF NOT EXISTS analytics_report_schedules (
    analytics_report_schedule_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_actual_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    owner_effective_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    schedule_name VARCHAR(180) NOT NULL,
    report_code VARCHAR(120) NOT NULL,
    criteria_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    cadence VARCHAR(24) NOT NULL CHECK (cadence IN (
        'daily', 'weekdays', 'weekly', 'monthly', 'quarterly', 'yearly'
    )),
    day_of_week SMALLINT NULL CHECK (day_of_week IS NULL OR day_of_week BETWEEN 0 AND 6),
    day_of_month SMALLINT NULL CHECK (day_of_month IS NULL OR day_of_month BETWEEN 1 AND 31),
    month_of_year SMALLINT NULL CHECK (month_of_year IS NULL OR month_of_year BETWEEN 1 AND 12),
    local_time TIME NOT NULL DEFAULT TIME '08:00',
    timezone_name VARCHAR(100) NOT NULL DEFAULT 'America/New_York',
    export_format VARCHAR(12) NOT NULL DEFAULT 'pdf' CHECK (export_format IN ('pdf', 'xlsx')),
    delivery_boundary VARCHAR(40) NOT NULL DEFAULT 'test_only' CHECK (delivery_boundary IN (
        'test_only', 'production_governed', 'locked'
    )),
    email_subject VARCHAR(500) NOT NULL DEFAULT '',
    email_message TEXT NOT NULL DEFAULT '',
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    next_run_at TIMESTAMPTZ NULL,
    last_started_at TIMESTAMPTZ NULL,
    last_completed_at TIMESTAMPTZ NULL,
    last_status VARCHAR(40) NOT NULL DEFAULT 'not_run',
    version INTEGER NOT NULL DEFAULT 1 CHECK (version > 0),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_analytics_report_schedules_due
    ON analytics_report_schedules(enabled, next_run_at);
CREATE INDEX IF NOT EXISTS ix_analytics_report_schedules_owner
    ON analytics_report_schedules(owner_actual_user_id, updated_at DESC);

CREATE TABLE IF NOT EXISTS analytics_report_schedule_recipients (
    analytics_report_schedule_recipient_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    analytics_report_schedule_id UUID NOT NULL REFERENCES analytics_report_schedules(analytics_report_schedule_id) ON DELETE CASCADE,
    recipient_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    recipient_name VARCHAR(240) NOT NULL DEFAULT '',
    recipient_email VARCHAR(320) NOT NULL,
    recipient_type VARCHAR(12) NOT NULL DEFAULT 'to' CHECK (recipient_type IN ('to', 'cc', 'bcc')),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_analytics_schedule_recipient_email
    ON analytics_report_schedule_recipients(
        analytics_report_schedule_id,
        lower(recipient_email),
        recipient_type
    );

CREATE TABLE IF NOT EXISTS analytics_report_schedule_runs (
    analytics_report_schedule_run_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    analytics_report_schedule_id UUID NULL REFERENCES analytics_report_schedules(analytics_report_schedule_id) ON DELETE SET NULL,
    schedule_name VARCHAR(180) NOT NULL,
    report_code VARCHAR(120) NOT NULL,
    owner_actual_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    started_at TIMESTAMPTZ NOT NULL,
    completed_at TIMESTAMPTZ NOT NULL,
    run_status VARCHAR(40) NOT NULL CHECK (run_status IN (
        'complete', 'partial', 'queued', 'suppressed', 'failed'
    )),
    recipient_count INTEGER NOT NULL DEFAULT 0 CHECK (recipient_count >= 0),
    sent_count INTEGER NOT NULL DEFAULT 0 CHECK (sent_count >= 0),
    queued_count INTEGER NOT NULL DEFAULT 0 CHECK (queued_count >= 0),
    failed_count INTEGER NOT NULL DEFAULT 0 CHECK (failed_count >= 0),
    diagnostic_code VARCHAR(120) NOT NULL DEFAULT '',
    diagnostic_message TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_analytics_schedule_runs_schedule
    ON analytics_report_schedule_runs(analytics_report_schedule_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_analytics_schedule_runs_status
    ON analytics_report_schedule_runs(run_status, created_at DESC);

CREATE TABLE IF NOT EXISTS analytics_report_schedule_delivery_attempts (
    analytics_report_schedule_delivery_attempt_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    analytics_report_schedule_run_id UUID NOT NULL REFERENCES analytics_report_schedule_runs(analytics_report_schedule_run_id) ON DELETE RESTRICT,
    enterprise_report_run_id UUID NULL REFERENCES enterprise_report_runs(enterprise_report_run_id) ON DELETE RESTRICT,
    recipient_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    recipient_email VARCHAR(320) NOT NULL,
    export_format VARCHAR(12) NOT NULL CHECK (export_format IN ('pdf', 'xlsx')),
    content_sha256 VARCHAR(64) NOT NULL DEFAULT '' CHECK (
        content_sha256 = '' OR content_sha256 ~ '^[0-9a-f]{64}$'
    ),
    delivery_status VARCHAR(40) NOT NULL CHECK (delivery_status IN (
        'sent', 'queued', 'suppressed', 'failed'
    )),
    provider_source VARCHAR(40) NOT NULL DEFAULT 'module_065',
    provider_message_id TEXT NOT NULL DEFAULT '',
    diagnostic_code VARCHAR(120) NOT NULL DEFAULT '',
    diagnostic_message TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_analytics_schedule_delivery_run
    ON analytics_report_schedule_delivery_attempts(analytics_report_schedule_run_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_analytics_schedule_delivery_report
    ON analytics_report_schedule_delivery_attempts(enterprise_report_run_id, created_at DESC);

CREATE TABLE IF NOT EXISTS analytics_user_report_activity (
    analytics_user_report_activity_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    report_code VARCHAR(120) NOT NULL,
    is_favorite BOOLEAN NOT NULL DEFAULT FALSE,
    view_count INTEGER NOT NULL DEFAULT 0 CHECK (view_count >= 0),
    last_viewed_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(user_id, report_code)
);

CREATE INDEX IF NOT EXISTS ix_analytics_user_activity_recent
    ON analytics_user_report_activity(user_id, last_viewed_at DESC NULLS LAST);
CREATE INDEX IF NOT EXISTS ix_analytics_user_activity_favorite
    ON analytics_user_report_activity(user_id, is_favorite, updated_at DESC);

CREATE OR REPLACE FUNCTION projectpulse060_block_analytics_schedule_evidence_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse060_analytics_schedule_immutable$
BEGIN
    RAISE EXCEPTION 'Analytics schedule-run and delivery evidence is immutable.';
END;
$projectpulse060_analytics_schedule_immutable$;

DROP TRIGGER IF EXISTS trg_projectpulse060_schedule_runs_immutable
    ON analytics_report_schedule_runs;
CREATE TRIGGER trg_projectpulse060_schedule_runs_immutable
BEFORE UPDATE OR DELETE ON analytics_report_schedule_runs
FOR EACH ROW EXECUTE FUNCTION projectpulse060_block_analytics_schedule_evidence_mutation();

DROP TRIGGER IF EXISTS trg_projectpulse060_schedule_delivery_immutable
    ON analytics_report_schedule_delivery_attempts;
CREATE TRIGGER trg_projectpulse060_schedule_delivery_immutable
BEFORE UPDATE OR DELETE ON analytics_report_schedule_delivery_attempts
FOR EACH ROW EXECUTE FUNCTION projectpulse060_block_analytics_schedule_evidence_mutation();

INSERT INTO app_permissions (
    permission_code,
    permission_name,
    module_code,
    permission_description
)
VALUES
    ('VIEW_ANALYTICS_DASHBOARDS', 'View Analytics Dashboards', '030', 'View role-scoped Analytics Center KPI, report-library, recent-report, source-quality, and delivery-health surfaces.'),
    ('VIEW_ANALYTICS_SCHEDULES', 'View Analytics Schedules', '030', 'View role-scoped recurring report schedules and immutable delivery history.'),
    ('MANAGE_ANALYTICS_SCHEDULES', 'Manage Analytics Schedules', '030', 'Create, update, disable, delete, and run role-scoped recurring Analytics Center reports.'),
    ('DELIVER_ANALYTICS_SCHEDULES', 'Deliver Analytics Schedules', '030', 'Authorize multiple-recipient Analytics delivery through Module 065 within the configured recipient boundary.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

-- Every role already authorized to view Analytics can use the dashboard and view its own schedules.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT DISTINCT existing.app_role_id, added.app_permission_id
FROM app_role_permissions existing
JOIN app_permissions source_permission
  ON source_permission.app_permission_id = existing.app_permission_id
 AND source_permission.permission_code = 'VIEW_ENTERPRISE_REPORTING'
CROSS JOIN app_permissions added
WHERE added.permission_code IN ('VIEW_ANALYTICS_DASHBOARDS', 'VIEW_ANALYTICS_SCHEDULES')
ON CONFLICT DO NOTHING;

-- Roles already authorized to run Analytics may create schedules. The service still
-- enforces effective-user report scope and View-As read-only behavior.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT DISTINCT existing.app_role_id, added.app_permission_id
FROM app_role_permissions existing
JOIN app_permissions source_permission
  ON source_permission.app_permission_id = existing.app_permission_id
 AND source_permission.permission_code = 'RUN_ENTERPRISE_REPORTING'
CROSS JOIN app_permissions added
WHERE added.permission_code = 'MANAGE_ANALYTICS_SCHEDULES'
ON CONFLICT DO NOTHING;

-- Multiple-recipient delivery is limited to operationally accountable roles. Other
-- users can schedule individual delivery to their own active ProjectPulse identity.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
CROSS JOIN app_permissions permission
WHERE upper(role.role_code) IN (
    'SUPER_ADMINISTRATOR', 'ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR',
    'ACCOUNTING', 'ACCOUNTING_BILLING', 'BILLING', 'FINANCE',
    'EXECUTIVE', 'EXECUTIVE_LEADERSHIP', 'MANAGER', 'ENGINEERING_MANAGER',
    'ENGINEERING_TEAM_LEAD', 'PROJECT_MANAGEMENT_LEAD',
    'PROJECT_MANAGEMENT_TEAM_LEAD', 'PM_TEAM_LEAD'
)
AND permission.permission_code = 'DELIVER_ANALYTICS_SCHEDULES'
ON CONFLICT DO NOTHING;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '060_analytics_center_enterprise_experience',
    'Analytics Center enterprise dashboard, favorites and recent activity, recurring PDF/XLSX schedules, per-recipient scope, Module 065 attachment delivery, and immutable schedule evidence',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
