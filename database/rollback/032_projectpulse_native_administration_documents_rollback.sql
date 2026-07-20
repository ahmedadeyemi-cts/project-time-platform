-- Destructive rollback for ProjectPulse migration 032.
-- Export retained native administration documents before applying this rollback.
BEGIN;
DROP TABLE IF EXISTS projectpulse_native_admin_document_revisions;
DROP TABLE IF EXISTS projectpulse_native_admin_documents;
COMMIT;
