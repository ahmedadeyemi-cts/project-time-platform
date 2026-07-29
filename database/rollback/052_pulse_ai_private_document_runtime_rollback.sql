-- Roll back Pulse AI Module 011 phase 011C.
-- This removes only objects introduced by migration 052.

BEGIN;

DELETE FROM app_role_permissions
WHERE app_permission_id IN (
    SELECT app_permission_id
    FROM app_permissions
    WHERE permission_code IN (
        'VIEW_PULSE_AI_DOCUMENT_RUNTIME',
        'MANAGE_PULSE_AI_DOCUMENT_PROCESSING',
        'APPROVE_PULSE_AI_DOCUMENT_VERSION',
        'REVOKE_PULSE_AI_DOCUMENT_INDEX',
        'VIEW_PULSE_AI_DOCUMENT_AUDIT'
    )
);

DELETE FROM app_feature_catalog
WHERE feature_code = 'PULSE_AI_PRIVATE_DOCUMENT_RUNTIME';

DELETE FROM app_permissions
WHERE permission_code IN (
    'VIEW_PULSE_AI_DOCUMENT_RUNTIME',
    'MANAGE_PULSE_AI_DOCUMENT_PROCESSING',
    'APPROVE_PULSE_AI_DOCUMENT_VERSION',
    'REVOKE_PULSE_AI_DOCUMENT_INDEX',
    'VIEW_PULSE_AI_DOCUMENT_AUDIT'
);

ALTER TABLE project_intake_documents
    DROP CONSTRAINT IF EXISTS fk_project_intake_documents_pulse_ai_active_version,
    DROP CONSTRAINT IF EXISTS fk_project_intake_documents_pulse_ai_last_job;

ALTER TABLE project_intake_documents
    DROP COLUMN IF EXISTS pulse_ai_processing_status,
    DROP COLUMN IF EXISTS pulse_ai_index_status,
    DROP COLUMN IF EXISTS pulse_ai_source_sha256,
    DROP COLUMN IF EXISTS pulse_ai_active_version_id,
    DROP COLUMN IF EXISTS pulse_ai_last_job_id,
    DROP COLUMN IF EXISTS pulse_ai_last_processed_at,
    DROP COLUMN IF EXISTS pulse_ai_canonical_version_label;

DROP TRIGGER IF EXISTS trg_projectpulse052_processing_events_immutable
    ON pulse_ai_document_processing_events;
DROP TRIGGER IF EXISTS trg_projectpulse052_scan_results_immutable
    ON pulse_ai_document_scan_results;

DROP TABLE IF EXISTS pulse_ai_document_processing_events;
DROP TABLE IF EXISTS pulse_ai_document_revocations;
DROP TABLE IF EXISTS pulse_ai_document_access_snapshots;
DROP TABLE IF EXISTS pulse_ai_document_index_receipts;
DROP TABLE IF EXISTS pulse_ai_document_chunks;
DROP TABLE IF EXISTS pulse_ai_document_sections;
DROP TABLE IF EXISTS pulse_ai_document_artifacts;
DROP TABLE IF EXISTS pulse_ai_document_processing_jobs;
DROP TABLE IF EXISTS pulse_ai_document_scan_results;
DROP TABLE IF EXISTS pulse_ai_document_versions;

DROP FUNCTION IF EXISTS projectpulse052_block_private_ai_evidence_mutation();

DELETE FROM schema_migrations
WHERE migration_id = '052_pulse_ai_private_document_runtime';

COMMIT;
