-- Roll back Pulse AI Module 011 migration 052.

BEGIN;

DELETE FROM app_role_permissions
WHERE app_permission_id IN (
    SELECT app_permission_id
    FROM app_permissions
    WHERE permission_code IN (
        'VIEW_PULSE_AI_DOCUMENT_RUNTIME',
        'QUEUE_PULSE_AI_DOCUMENT_PROCESSING',
        'CANCEL_PULSE_AI_DOCUMENT_PROCESSING',
        'RETRY_PULSE_AI_DOCUMENT_PROCESSING',
        'APPROVE_PULSE_AI_DOCUMENT_VERSION'
    )
);

DELETE FROM app_feature_catalog
WHERE feature_code = 'PULSE_AI_PRIVATE_DOCUMENT_RUNTIME';

DELETE FROM app_permissions
WHERE permission_code IN (
    'VIEW_PULSE_AI_DOCUMENT_RUNTIME',
    'QUEUE_PULSE_AI_DOCUMENT_PROCESSING',
    'CANCEL_PULSE_AI_DOCUMENT_PROCESSING',
    'RETRY_PULSE_AI_DOCUMENT_PROCESSING',
    'APPROVE_PULSE_AI_DOCUMENT_VERSION'
);

DROP TRIGGER IF EXISTS trg_pulse_ai_052_processing_events_immutable
    ON pulse_ai_document_processing_events;
DROP FUNCTION IF EXISTS pulse_ai_052_block_processing_event_mutation();

DROP TRIGGER IF EXISTS trg_pulse_ai_052_processing_jobs_updated_at
    ON pulse_ai_document_processing_jobs;
DROP FUNCTION IF EXISTS pulse_ai_052_touch_job_updated_at();

ALTER TABLE project_intake_documents
    DROP CONSTRAINT IF EXISTS fk_project_intake_documents_pulse_ai_active_version;

DROP TABLE IF EXISTS pulse_ai_document_processing_events;
DROP TABLE IF EXISTS pulse_ai_document_chunks;
DROP TABLE IF EXISTS pulse_ai_document_sections;
DROP TABLE IF EXISTS pulse_ai_document_versions;
DROP TABLE IF EXISTS pulse_ai_document_processing_jobs;

ALTER TABLE project_intake_documents
    DROP CONSTRAINT IF EXISTS ck_project_intake_documents_pulse_ai_processing_status,
    DROP COLUMN IF EXISTS pulse_ai_processing_status,
    DROP COLUMN IF EXISTS pulse_ai_classification,
    DROP COLUMN IF EXISTS pulse_ai_document_revision,
    DROP COLUMN IF EXISTS pulse_ai_effective_at,
    DROP COLUMN IF EXISTS pulse_ai_superseded_by_document_id,
    DROP COLUMN IF EXISTS pulse_ai_active_version_id,
    DROP COLUMN IF EXISTS pulse_ai_processing_error_code,
    DROP COLUMN IF EXISTS pulse_ai_processing_updated_at;

DELETE FROM schema_migrations
WHERE migration_id = '052_pulse_ai_private_document_runtime';

COMMIT;
