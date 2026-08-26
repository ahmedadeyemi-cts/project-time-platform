-- ProjectPulse 097 — identity-safe private document admission.
--
-- Migration 057 installed an automatic project-document queue trigger that
-- populated requested_by_user_id but left actual_user_id and effective_user_id
-- NULL. The private worker correctly rejects such work as
-- authorization_identity_missing. Current FlowHive/Forge admission is performed
-- by authenticated project-scoped application code, while background admission
-- uses the explicitly configured document service principal. The legacy trigger
-- is therefore retired rather than weakening the worker authorization boundary.

BEGIN;

DO $projectpulse097_prerequisites$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id = '057_module_001_multi_timer_document_grounded_ai'
    ) THEN
        RAISE EXCEPTION 'Migration 097 requires migration 057 first.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id = '096_project_planning_document_authority'
    ) THEN
        RAISE EXCEPTION 'Migration 097 requires migration 096 first.';
    END IF;
END;
$projectpulse097_prerequisites$;

-- Remove the obsolete database-side queue authority. Do not recreate this
-- trigger in rollback: doing so would reintroduce identity-less private work.
DROP TRIGGER IF EXISTS trg_module001_057_queue_project_ai_document_insert
    ON project_intake_documents;
DROP TRIGGER IF EXISTS trg_module001_057_queue_project_ai_document_update
    ON project_intake_documents;
DROP FUNCTION IF EXISTS module001_057_queue_project_ai_document();

-- Active legacy jobs can block the authenticated recovery path through the
-- active-job uniqueness guard. Terminalize only those identity-less active rows.
-- Already-terminal historical rows remain untouched as audit evidence.
WITH retired AS (
    UPDATE pulse_ai_document_processing_jobs
       SET job_status = 'failed',
           completed_at = COALESCE(completed_at, NOW()),
           cancellation_requested = FALSE,
           lease_owner = '',
           lease_token = NULL,
           lease_heartbeat_at = NULL,
           lease_expires_at = NULL,
           diagnostic_code = 'legacy_identityless_queue_retired',
           diagnostic_message = 'Legacy Module 001 identity-less automatic queueing was retired; authenticated or service-principal admission is required.',
           updated_at = NOW()
     WHERE requested_purpose = 'project_ai_generation_grounding'
       AND actual_user_id IS NULL
       AND effective_user_id IS NULL
       AND job_status IN (
           'queued','scanning','extracting','awaiting_ocr','embedding',
           'indexing','retry_wait','cancel_requested'
       )
    RETURNING project_intake_document_id
)
UPDATE project_intake_documents AS document
   SET pulse_ai_processing_status = CASE
           WHEN document.pulse_ai_processing_status = 'ready' THEN 'ready'
           ELSE 'failed'
       END,
       pulse_ai_processing_error_code = CASE
           WHEN document.pulse_ai_processing_status = 'ready'
               THEN document.pulse_ai_processing_error_code
           ELSE 'legacy_identityless_queue_retired'
       END,
       pulse_ai_processing_updated_at = NOW()
 WHERE document.project_intake_document_id IN (
     SELECT project_intake_document_id FROM retired
 );

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '097_project_planning_identity_safe_admission',
    'Retire legacy identity-less project AI document queueing and require governed authenticated or service-principal admission',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
