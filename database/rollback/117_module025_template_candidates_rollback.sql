BEGIN;
SELECT pg_advisory_xact_lock(250117);
-- Do not remove retained originals or their audit facts during rollback.
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM module025_template_candidates) THEN
        RAISE EXCEPTION 'Rollback refused: retained template candidates exist; preserve this table when rolling back the application';
    END IF;
END $$;
DROP TABLE IF EXISTS module025_template_candidates;
DROP FUNCTION IF EXISTS module025_protect_template_candidate();
DELETE FROM schema_migrations WHERE migration_id='117_module025_template_candidates';
COMMIT;
