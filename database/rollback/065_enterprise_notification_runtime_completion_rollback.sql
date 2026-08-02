-- Roll back ProjectPulse migration 065.
-- Restores the migration-064 fully immutable run-history posture.

BEGIN;

DO $projectpulse065_rollback_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL THEN
        RAISE EXCEPTION 'Migration 065 rollback requires public.schema_migrations.';
    END IF;
    IF to_regclass('public.enterprise_notification_run_history') IS NULL THEN
        RAISE EXCEPTION 'Migration 065 rollback requires enterprise_notification_run_history.';
    END IF;
END;
$projectpulse065_rollback_prerequisites$;

DROP TRIGGER IF EXISTS trg_enterprise_notification_run_history_immutable
    ON enterprise_notification_run_history;
DROP FUNCTION IF EXISTS projectpulse065_guard_enterprise_notification_run_history();

CREATE OR REPLACE FUNCTION projectpulse064_block_enterprise_notification_evidence_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse064_immutable$
BEGIN
    RAISE EXCEPTION 'Enterprise notification orchestration evidence is immutable.';
END;
$projectpulse064_immutable$;

CREATE TRIGGER trg_enterprise_notification_run_history_immutable
BEFORE UPDATE OR DELETE ON enterprise_notification_run_history
FOR EACH ROW EXECUTE FUNCTION projectpulse064_block_enterprise_notification_evidence_mutation();

DELETE FROM schema_migrations
WHERE migration_id = '065_enterprise_notification_runtime_completion';

COMMIT;
