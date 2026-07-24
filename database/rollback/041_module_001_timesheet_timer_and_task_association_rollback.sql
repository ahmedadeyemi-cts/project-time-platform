-- Roll back ProjectPulse migration 041 Module 001 timer and task association foundation.
-- Fail closed after operational timer, association, audit, or submission-attribution data exists.
BEGIN;

DO $module001_041_rollback_guard$
DECLARE
    v_timer_count BIGINT;
    v_segment_count BIGINT;
    v_association_count BIGINT;
    v_audit_count BIGINT;
    v_submission_count BIGINT;
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id = '041_module_001_timesheet_timer_and_task_association'
    ) THEN
        RAISE EXCEPTION 'Migration 041 is not registered and cannot be rolled back.';
    END IF;

    SELECT COUNT(*) INTO v_timer_count FROM module001_timer_sessions;
    SELECT COUNT(*) INTO v_segment_count FROM module001_timer_daily_segments;
    SELECT COUNT(*) INTO v_association_count FROM module001_timesheet_entry_associations;
    SELECT COUNT(*) INTO v_audit_count FROM module001_timer_audit_events;
    SELECT COUNT(*) INTO v_submission_count
    FROM timesheets
    WHERE submitted_by_user_id IS NOT NULL
       OR NULLIF(BTRIM(submission_reason), '') IS NOT NULL;

    IF v_timer_count + v_segment_count + v_association_count + v_audit_count + v_submission_count > 0 THEN
        RAISE EXCEPTION
            'Migration 041 rollback blocked: timer=% segment=% association=% audit=% attributed_submission=% operational record(s) exist.',
            v_timer_count, v_segment_count, v_association_count, v_audit_count, v_submission_count;
    END IF;
END;
$module001_041_rollback_guard$;

DROP TRIGGER IF EXISTS trg_module001_041_timer_audit_immutable ON module001_timer_audit_events;
DROP TRIGGER IF EXISTS trg_module001_041_touch_association ON module001_timesheet_entry_associations;
DROP TRIGGER IF EXISTS trg_module001_041_touch_timer ON module001_timer_sessions;

DROP FUNCTION IF EXISTS module001_041_block_audit_mutation();
DROP FUNCTION IF EXISTS module001_041_touch_association();
DROP FUNCTION IF EXISTS module001_041_touch_timer();

DROP TABLE IF EXISTS module001_timer_audit_events;
DROP TABLE IF EXISTS module001_timer_daily_segments;
DROP TABLE IF EXISTS module001_timesheet_entry_associations;
DROP TABLE IF EXISTS module001_timer_sessions;

ALTER TABLE timesheets
    DROP COLUMN IF EXISTS submission_reason,
    DROP COLUMN IF EXISTS submitted_by_user_id;

UPDATE scoped_role_policy_modules
SET module_name = 'Time Entry'
WHERE module_code = '001'
  AND module_name = 'Timesheet';

DELETE FROM schema_migrations
WHERE migration_id = '041_module_001_timesheet_timer_and_task_association';

COMMIT;
