-- Project Health Dashboard migration 068 rollback
-- Removes only the standalone Module 006 persistence introduced by migration 068.

BEGIN;

DROP TRIGGER IF EXISTS trg_module006_pipeline_updates_immutable
    ON module006_pipeline_updates;
DROP FUNCTION IF EXISTS projectpulse068_block_pipeline_update_mutation();
DROP TABLE IF EXISTS module006_pipeline_updates;
DROP TABLE IF EXISTS module006_pipeline_records;

DELETE FROM schema_migrations
WHERE migration_id = '068_module006_standalone_pipeline_management';

COMMIT;
