-- Guarded rollback for ProjectPulse migration 103.
-- Refuse destructive rollback after any FlowHive PSA evidence exists.

BEGIN;

DO $projectpulse103_rollback_guard$
DECLARE
    evidence_count BIGINT := 0;
BEGIN
    IF to_regclass('public.project_flowhive_raid_events') IS NOT NULL THEN
        EXECUTE 'SELECT count(*) FROM project_flowhive_raid_events' INTO evidence_count;
        IF evidence_count > 0 THEN
            RAISE EXCEPTION 'Rollback refused: Project FlowHive RAID audit events exist.';
        END IF;
    END IF;
    IF to_regclass('public.project_flowhive_meetings') IS NOT NULL THEN
        EXECUTE 'SELECT count(*) FROM project_flowhive_meetings' INTO evidence_count;
        IF evidence_count > 0 THEN
            RAISE EXCEPTION 'Rollback refused: Project FlowHive meeting records exist.';
        END IF;
    END IF;
    IF to_regclass('public.project_flowhive_meeting_events') IS NOT NULL THEN
        EXECUTE 'SELECT count(*) FROM project_flowhive_meeting_events' INTO evidence_count;
        IF evidence_count > 0 THEN
            RAISE EXCEPTION 'Rollback refused: Project FlowHive meeting audit events exist.';
        END IF;
    END IF;
    IF to_regclass('public.project_flowhive_task_reminder_preferences') IS NOT NULL THEN
        EXECUTE 'SELECT count(*) FROM project_flowhive_task_reminder_preferences' INTO evidence_count;
        IF evidence_count > 0 THEN
            RAISE EXCEPTION 'Rollback refused: Project FlowHive task reminder preferences exist.';
        END IF;
    END IF;
    IF to_regclass('public.project_flowhive_task_reminder_events') IS NOT NULL THEN
        EXECUTE 'SELECT count(*) FROM project_flowhive_task_reminder_events' INTO evidence_count;
        IF evidence_count > 0 THEN
            RAISE EXCEPTION 'Rollback refused: Project FlowHive task reminder evidence exists.';
        END IF;
    END IF;
END;
$projectpulse103_rollback_guard$;

DROP TRIGGER IF EXISTS trg_project_flowhive_raid_events_immutable_103 ON project_flowhive_raid_events;
DROP TRIGGER IF EXISTS trg_project_flowhive_meeting_events_immutable_103 ON project_flowhive_meeting_events;
DROP TRIGGER IF EXISTS trg_project_flowhive_task_reminder_events_immutable_103 ON project_flowhive_task_reminder_events;
DROP TRIGGER IF EXISTS trg_project_flowhive_raid_audit_103 ON project_flowhive_raid_items;
DROP TRIGGER IF EXISTS trg_project_flowhive_meetings_touch_103 ON project_flowhive_meetings;
DROP TRIGGER IF EXISTS trg_project_flowhive_task_reminder_preferences_touch_103 ON project_flowhive_task_reminder_preferences;

DROP FUNCTION IF EXISTS projectpulse103_block_immutable_evidence();
DROP FUNCTION IF EXISTS projectpulse103_capture_raid_event();
DROP FUNCTION IF EXISTS projectpulse103_touch_timestamp();

DROP TABLE IF EXISTS project_flowhive_task_reminder_events;
DROP TABLE IF EXISTS project_flowhive_task_reminder_preferences;
DROP TABLE IF EXISTS project_flowhive_meeting_events;
DROP TABLE IF EXISTS project_flowhive_meetings;
DROP TABLE IF EXISTS project_flowhive_raid_events;

DELETE FROM app_permissions
WHERE permission_code IN (
    'MANAGE_FLOWHIVE_MEETINGS_066',
    'MANAGE_FLOWHIVE_TASK_REMINDERS_066',
    'VIEW_FLOWHIVE_AUDIT_066'
);

DELETE FROM schema_migrations
WHERE migration_id = '103_module_066_flowhive_enterprise_psa_revamp';

COMMIT;
