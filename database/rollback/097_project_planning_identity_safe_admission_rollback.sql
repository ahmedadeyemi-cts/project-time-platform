-- ProjectPulse 097 fail-safe rollback.
--
-- The migration registration can be removed for release bookkeeping, but the
-- retired migration-057 queue trigger is intentionally not restored. Restoring
-- it would recreate private-document jobs without an authorization identity.

BEGIN;

DELETE FROM schema_migrations
WHERE migration_id = '097_project_planning_identity_safe_admission';

COMMIT;
