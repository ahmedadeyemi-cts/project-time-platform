-- Guarded rollback for ProjectPulse migration 067.
-- Immutable PM acceptance evidence is never silently removed.

BEGIN;

DO $projectpulse067_rollback_guard$
BEGIN
    IF to_regclass('public.project_expense_upload_acceptances') IS NOT NULL
       AND EXISTS (SELECT 1 FROM project_expense_upload_acceptances) THEN
        RAISE EXCEPTION 'Rollback blocked: immutable PM expense acceptance evidence exists.';
    END IF;
END;
$projectpulse067_rollback_guard$;

DROP TRIGGER IF EXISTS trg_project_expense_acceptance_immutable
    ON project_expense_upload_acceptances;
DROP FUNCTION IF EXISTS projectpulse067_block_expense_acceptance_mutation();
DROP TRIGGER IF EXISTS trg_project_expense_acceptance_validate_insert
    ON project_expense_upload_acceptances;
DROP FUNCTION IF EXISTS projectpulse067_validate_expense_acceptance_insert();
DROP TABLE IF EXISTS project_expense_upload_acceptances;
DELETE FROM schema_migrations
WHERE migration_id = '067_uat_expense_lifecycle_work_identifiers';

COMMIT;
