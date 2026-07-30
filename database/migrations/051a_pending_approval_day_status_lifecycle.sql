-- ProjectPulse migration 051A
-- Completes the day-level approval lifecycle already used by Module 002 and
-- the scoped role-policy workflow: Manager -> PM -> PTC final -> Accounting.
-- This migration changes no existing row and is safe to apply repeatedly.

BEGIN;

DO $projectpulse051a_required_table$
BEGIN
    IF to_regclass('public.timesheet_day_statuses') IS NULL THEN
        RAISE EXCEPTION 'Migration 051A requires public.timesheet_day_statuses from migration 007.';
    END IF;

    IF to_regclass('public.schema_migrations') IS NULL THEN
        RAISE EXCEPTION 'Migration 051A requires public.schema_migrations.';
    END IF;
END;
$projectpulse051a_required_table$;

LOCK TABLE timesheet_day_statuses IN SHARE ROW EXCLUSIVE MODE;

DO $projectpulse051a_status_preflight$
DECLARE
    unsupported_statuses TEXT;
BEGIN
    SELECT string_agg(status, ', ' ORDER BY status)
    INTO unsupported_statuses
    FROM (
        SELECT DISTINCT status
        FROM timesheet_day_statuses
        WHERE status NOT IN (
            'draft',
            'submitted',
            'manager_approved',
            'manager_declined',
            'pm_approved',
            'pm_declined',
            'accounting_ready',
            'reconciled',
            'locked'
        )
    ) unsupported;

    IF unsupported_statuses IS NOT NULL THEN
        RAISE EXCEPTION
            'Migration 051A found unsupported timesheet day statuses: %',
            unsupported_statuses;
    END IF;
END;
$projectpulse051a_status_preflight$;

ALTER TABLE timesheet_day_statuses
    DROP CONSTRAINT IF EXISTS chk_timesheet_day_status;

ALTER TABLE timesheet_day_statuses
    ADD CONSTRAINT chk_timesheet_day_status
    CHECK (status IN (
        'draft',
        'submitted',
        'manager_approved',
        'manager_declined',
        'pm_approved',
        'pm_declined',
        'accounting_ready',
        'reconciled',
        'locked'
    ));

CREATE INDEX IF NOT EXISTS ix_timesheet_day_statuses_pending_approval_stage
    ON timesheet_day_statuses(status, work_date, timesheet_id, user_id)
    WHERE status IN ('submitted', 'manager_approved', 'pm_approved');

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '051a_pending_approval_day_status_lifecycle',
    'Authorize Manager, Project Manager, PTC-final, Accounting, reconciliation, and lock states for day-level approvals',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
