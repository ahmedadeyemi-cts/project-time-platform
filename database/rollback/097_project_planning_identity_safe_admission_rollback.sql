-- ProjectPulse 097 fail-safe rollback.
--
-- The migration registration and retry-exhaustion invariants can be removed for
-- release bookkeeping, but the retired migration-057 queue trigger is
-- intentionally not restored. Restoring it would recreate private-document jobs
-- without an authorization identity.

BEGIN;

DROP TRIGGER IF EXISTS trg_projectpulse097_document_retry_exhaustion
    ON project_intake_documents;
DROP FUNCTION IF EXISTS projectpulse097_enforce_document_retry_exhaustion();

DROP TRIGGER IF EXISTS trg_projectpulse097_private_retry_exhaustion
    ON pulse_ai_document_processing_jobs;
DROP FUNCTION IF EXISTS projectpulse097_enforce_private_retry_exhaustion();

DELETE FROM schema_migrations
WHERE migration_id = '097_project_planning_identity_safe_admission';

COMMIT;
