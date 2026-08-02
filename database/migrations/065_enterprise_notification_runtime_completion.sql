-- ProjectPulse migration 065
-- Complete the Module 065 enterprise notification runtime safely.
--
-- Migration 064 made run-history evidence fully immutable, including the one
-- required transition from a newly inserted `running` record to its final
-- completed, partial, or failed state. This migration permits exactly that
-- transition while keeping identity, origin, and finalized evidence immutable.

BEGIN;

DO $projectpulse065_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL THEN
        RAISE EXCEPTION 'Migration 065 requires public.schema_migrations.';
    END IF;
    IF NOT EXISTS (
        SELECT 1
        FROM schema_migrations
        WHERE migration_id = '064_module_065_enterprise_notification_orchestration'
    ) THEN
        RAISE EXCEPTION 'Migration 065 requires migration 064.';
    END IF;
    IF to_regclass('public.enterprise_notification_run_history') IS NULL THEN
        RAISE EXCEPTION 'Migration 065 requires enterprise_notification_run_history.';
    END IF;
END;
$projectpulse065_prerequisites$;

CREATE OR REPLACE FUNCTION projectpulse065_guard_enterprise_notification_run_history()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse065_run_history$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'Enterprise notification run evidence is immutable.';
    END IF;

    IF OLD.run_status <> 'running'
       OR NEW.run_status NOT IN ('completed', 'partial', 'failed') THEN
        RAISE EXCEPTION 'Enterprise notification run evidence permits only the initial running-to-final transition.';
    END IF;

    IF NEW.enterprise_notification_run_history_id IS DISTINCT FROM OLD.enterprise_notification_run_history_id
       OR NEW.run_type IS DISTINCT FROM OLD.run_type
       OR NEW.started_by_user_id IS DISTINCT FROM OLD.started_by_user_id
       OR NEW.started_at IS DISTINCT FROM OLD.started_at
       OR NEW.correlation_id IS DISTINCT FROM OLD.correlation_id THEN
        RAISE EXCEPTION 'Enterprise notification run identity and origin evidence are immutable.';
    END IF;

    IF NEW.completed_at IS NULL OR NEW.completed_at < OLD.started_at THEN
        RAISE EXCEPTION 'Enterprise notification run completion evidence requires a valid completion timestamp.';
    END IF;

    RETURN NEW;
END;
$projectpulse065_run_history$;

DROP TRIGGER IF EXISTS trg_enterprise_notification_run_history_immutable
    ON enterprise_notification_run_history;
CREATE TRIGGER trg_enterprise_notification_run_history_immutable
BEFORE UPDATE OR DELETE ON enterprise_notification_run_history
FOR EACH ROW EXECUTE FUNCTION projectpulse065_guard_enterprise_notification_run_history();

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '065_enterprise_notification_runtime_completion',
    'Permit exactly one running-to-final enterprise notification run-history transition while preserving immutable finalized evidence',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
