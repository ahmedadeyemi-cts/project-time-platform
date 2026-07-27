-- Guarded rollback for ProjectPulse migration 048.
BEGIN;

DO $projectpulse048_rollback_guard$
BEGIN
    IF to_regclass('public.projectpulse_system_audit_events') IS NOT NULL
       AND EXISTS (SELECT 1 FROM projectpulse_system_audit_events) THEN
        RAISE EXCEPTION
            'Rollback blocked: immutable ProjectPulse system audit evidence exists.';
    END IF;

    IF to_regclass('public.user_admin_manager_team_assignments') IS NOT NULL
       AND EXISTS (
            SELECT 1
            FROM user_admin_manager_team_assignments
            WHERE is_active = TRUE
       ) THEN
        RAISE EXCEPTION
            'Rollback blocked: active manager-to-team assignments exist.';
    END IF;
END;
$projectpulse048_rollback_guard$;

DROP INDEX IF EXISTS ix_user_admin_manager_team_team;
DROP INDEX IF EXISTS ix_user_admin_manager_team_manager;
DROP INDEX IF EXISTS ux_user_admin_one_active_manager_per_team;
DROP TABLE IF EXISTS user_admin_manager_team_assignments;

DROP INDEX IF EXISTS ix_projectpulse_system_audit_actor;
DROP INDEX IF EXISTS ix_projectpulse_system_audit_category_status;
DROP INDEX IF EXISTS ix_projectpulse_system_audit_event_time;
DROP TRIGGER IF EXISTS trg_projectpulse048_system_audit_immutable
ON projectpulse_system_audit_events;
DROP FUNCTION IF EXISTS projectpulse048_block_system_audit_mutation();
DROP TABLE IF EXISTS projectpulse_system_audit_events;

DELETE FROM schema_migrations
WHERE migration_id = '048_admin_audit_and_manager_team_scope';

COMMIT;
