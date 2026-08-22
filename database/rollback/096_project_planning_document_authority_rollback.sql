-- Rollback for ProjectPulse migration 096.
BEGIN;

DROP VIEW IF EXISTS current_project_planning_document_authority;
DROP FUNCTION IF EXISTS projectpulse_reconcile_project_planning_document_authority(
    uuid,
    uuid,
    uuid,
    text,
    text,
    uuid,
    text,
    text,
    text,
    text,
    text,
    text,
    jsonb
);
DROP TABLE IF EXISTS project_planning_document_authority;

DO $$
BEGIN
    IF to_regclass('public.schema_migrations') IS NOT NULL
       AND EXISTS (
           SELECT 1
             FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'schema_migrations'
              AND column_name = 'migration_id'
       ) THEN
        EXECUTE 'DELETE FROM schema_migrations WHERE migration_id = $1'
           USING '096_project_planning_document_authority';
    END IF;
END;
$$;

COMMIT;
