-- ProjectPulse migration 051A guarded rollback
-- Rollback is refused after any row has advanced beyond the pre-051A lifecycle.

BEGIN;

DO $projectpulse051a_rollback_required_table$
BEGIN
    IF to_regclass('public.timesheet_day_statuses') IS NULL THEN
        RAISE EXCEPTION 'Migration 051A rollback requires public.timesheet_day_statuses.';
    END IF;
END;
$projectpulse051a_rollback_required_table$;

LOCK TABLE timesheet_day_statuses IN SHARE ROW EXCLUSIVE MODE;

DO $projectpulse051a_rollback_guard$
DECLARE
    later_status_count BIGINT;
    later_statuses TEXT;
BEGIN
    SELECT COUNT(*), string_agg(status, ', ' ORDER BY status)
    INTO later_status_count, later_statuses
    FROM (
        SELECT status, COUNT(*) AS row_count
        FROM timesheet_day_statuses
        WHERE status NOT IN (
            'draft',
            'submitted',
            'manager_approved',
            'manager_declined',
            'pm_declined'
        )
        GROUP BY status
    ) later;

    IF later_status_count > 0 THEN
        RAISE EXCEPTION
            'Migration 051A rollback blocked: later approval lifecycle statuses are in use (%).',
            later_statuses;
    END IF;
END;
$projectpulse051a_rollback_guard$;

DROP INDEX IF EXISTS ix_timesheet_day_statuses_pending_approval_stage;

ALTER TABLE timesheet_day_statuses
    DROP CONSTRAINT IF EXISTS chk_timesheet_day_status;

ALTER TABLE timesheet_day_statuses
    ADD CONSTRAINT chk_timesheet_day_status
    CHECK (status IN (
        'draft',
        'submitted',
        'manager_approved',
        'manager_declined',
        'pm_declined'
    ));

DELETE FROM schema_migrations
WHERE migration_id = '051a_pending_approval_day_status_lifecycle';

COMMIT;
