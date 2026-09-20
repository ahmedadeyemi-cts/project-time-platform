-- Disable new coverage/acknowledgement endpoints. Retain all coverage metadata,
-- current owners, working content, immutable events and document versions.
-- There is deliberately no automatic ownership restoration during rollback.
BEGIN;
SELECT pg_advisory_xact_lock(250119);
DELETE FROM schema_migrations WHERE migration_id='119_module025_temporary_handoffs';
COMMIT;
