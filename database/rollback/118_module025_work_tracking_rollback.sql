-- Disable work tracking without deleting immutable scheduling history.
BEGIN;
SELECT pg_advisory_xact_lock(250118);
DELETE FROM schema_migrations WHERE migration_id='118_module025_work_tracking';
-- Retained table and mutation guards intentionally remain for audit and reapply.
COMMIT;
