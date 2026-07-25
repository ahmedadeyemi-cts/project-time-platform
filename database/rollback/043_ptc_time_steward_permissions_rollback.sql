-- Guarded rollback for migration 043.
-- Fails closed after any operational time-steward evidence exists.
BEGIN;

DO $projectpulse043_rollback_guard$
BEGIN
    IF to_regclass('public.scoped_time_management_events') IS NOT NULL
       AND EXISTS (SELECT 1 FROM scoped_time_management_events)
    THEN
        RAISE EXCEPTION
            'Rollback 043 is blocked because PTC time-management audit evidence exists.';
    END IF;
END;
$projectpulse043_rollback_guard$;

DELETE FROM scoped_role_policy_grants
WHERE source_notes = 'PTC_TIME_STEWARD_043'
   OR conditions->>'source' = '043_ptc_time_steward_permissions';

DROP TRIGGER IF EXISTS trg_projectpulse043_time_management_audit_immutable
ON scoped_time_management_events;
DROP TABLE IF EXISTS scoped_time_management_events;

ALTER TABLE module001_timesheet_entry_associations
    DROP CONSTRAINT IF EXISTS chk_module001_association_source;
ALTER TABLE module001_timesheet_entry_associations
    ADD CONSTRAINT chk_module001_association_source CHECK (
        association_source IN ('EXISTING_ENTRY','WORK_QUEUE','TIMER','CALENDAR')
    );

DELETE FROM scoped_role_policy_actions action_row
WHERE action_row.action_code IN (
    'TIME_VIEW_ON_BEHALF',
    'TIME_UNSUBMIT',
    'TIME_DELETE_ON_BEHALF',
    'TIME_TASK_CREATE',
    'TIME_TASK_ASSIGN'
)
AND NOT EXISTS (
    SELECT 1
    FROM scoped_role_policy_grants grant_row
    WHERE grant_row.action_code = action_row.action_code
);

DELETE FROM schema_migrations
WHERE migration_id = '043_ptc_time_steward_permissions';

COMMIT;
