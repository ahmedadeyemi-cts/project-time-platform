-- ProjectPulse 097 — identity-safe private document admission and bounded retry exhaustion.
--
-- Migration 057 installed an automatic project-document queue trigger that
-- populated requested_by_user_id but left actual_user_id and effective_user_id
-- NULL. The private worker correctly rejects such work as
-- authorization_identity_missing. Current FlowHive/Forge admission is performed
-- by authenticated project-scoped application code, while background admission
-- uses the explicitly configured document service principal. The legacy trigger
-- is therefore retired rather than weakening the worker authorization boundary.
--
-- The private-processing worker also uses retry_wait as a bounded retry state.
-- A retry_wait row whose attempt_count has already reached maximum_attempts is
-- not claimable by the worker and must therefore be terminal. This migration
-- repairs existing impossible retry_wait rows and installs database invariants
-- so exhausted work cannot remain permanently non-terminal.

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

-- A retry_wait job at or above maximum_attempts can never satisfy the worker's
-- claim predicate (attempt_count < maximum_attempts). Repair any such rows now
-- and preserve the most specific existing diagnostic when one is available.
WITH exhausted AS (
    UPDATE pulse_ai_document_processing_jobs
       SET job_status = 'failed',
           completed_at = COALESCE(completed_at, NOW()),
           cancellation_requested = FALSE,
           lease_owner = '',
           lease_token = NULL,
           lease_heartbeat_at = NULL,
           lease_expires_at = NULL,
           diagnostic_code = CASE
               WHEN COALESCE(diagnostic_code, '') = '' THEN 'retry_attempts_exhausted'
               ELSE diagnostic_code
           END,
           diagnostic_message = CASE
               WHEN COALESCE(diagnostic_message, '') = ''
                   THEN 'Private document processing exhausted its bounded retry attempts.'
               ELSE diagnostic_message
           END,
           updated_at = NOW()
     WHERE job_status = 'retry_wait'
       AND attempt_count >= maximum_attempts
    RETURNING project_intake_document_id, diagnostic_code
), exhausted_document AS (
    SELECT project_intake_document_id,
           MAX(NULLIF(diagnostic_code, '')) AS diagnostic_code
      FROM exhausted
     GROUP BY project_intake_document_id
)
UPDATE project_intake_documents AS document
   SET pulse_ai_processing_status = CASE
           WHEN document.pulse_ai_processing_status = 'ready' THEN 'ready'
           ELSE 'failed'
       END,
       pulse_ai_processing_error_code = CASE
           WHEN document.pulse_ai_processing_status = 'ready'
               THEN document.pulse_ai_processing_error_code
           WHEN COALESCE(document.pulse_ai_processing_error_code, '') <> ''
               THEN document.pulse_ai_processing_error_code
           ELSE COALESCE(exhausted_document.diagnostic_code, 'retry_attempts_exhausted')
       END,
       pulse_ai_processing_updated_at = NOW()
  FROM exhausted_document
 WHERE document.project_intake_document_id = exhausted_document.project_intake_document_id;

-- Enforce the job-level invariant for every future processing attempt. The
-- application may request retry_wait after a bounded failure; if the attempt
-- budget is already exhausted, the row becomes failed atomically instead of
-- entering an unclaimable non-terminal state.
CREATE OR REPLACE FUNCTION projectpulse097_enforce_private_retry_exhaustion()
RETURNS trigger
LANGUAGE plpgsql
AS $projectpulse097_retry_job$
BEGIN
    IF NEW.job_status = 'retry_wait'
       AND NEW.attempt_count >= NEW.maximum_attempts THEN
        NEW.job_status := 'failed';
        NEW.completed_at := COALESCE(NEW.completed_at, NOW());
        NEW.cancellation_requested := FALSE;
        NEW.lease_owner := '';
        NEW.lease_token := NULL;
        NEW.lease_heartbeat_at := NULL;
        NEW.lease_expires_at := NULL;
        IF COALESCE(NEW.diagnostic_code, '') = '' THEN
            NEW.diagnostic_code := 'retry_attempts_exhausted';
        END IF;
        IF COALESCE(NEW.diagnostic_message, '') = '' THEN
            NEW.diagnostic_message := 'Private document processing exhausted its bounded retry attempts.';
        END IF;
    END IF;
    RETURN NEW;
END;
$projectpulse097_retry_job$;

DROP TRIGGER IF EXISTS trg_projectpulse097_private_retry_exhaustion
    ON pulse_ai_document_processing_jobs;
CREATE TRIGGER trg_projectpulse097_private_retry_exhaustion
BEFORE INSERT OR UPDATE OF job_status, attempt_count, maximum_attempts
ON pulse_ai_document_processing_jobs
FOR EACH ROW
EXECUTE FUNCTION projectpulse097_enforce_private_retry_exhaustion();

-- CompleteTerminalAsync updates the job first and the document row second. Keep
-- the document projection consistent with the job invariant so FlowHive sees a
-- terminal failure and its governed explicit retry can create a fresh attempt.
CREATE OR REPLACE FUNCTION projectpulse097_enforce_document_retry_exhaustion()
RETURNS trigger
LANGUAGE plpgsql
AS $projectpulse097_retry_document$
DECLARE
    exhausted_diagnostic text;
BEGIN
    IF NEW.pulse_ai_processing_status = 'retry_wait' THEN
        SELECT NULLIF(job.diagnostic_code, '')
          INTO exhausted_diagnostic
          FROM pulse_ai_document_processing_jobs job
         WHERE job.project_intake_document_id = NEW.project_intake_document_id
           AND job.job_status = 'failed'
           AND job.attempt_count >= job.maximum_attempts
         ORDER BY job.updated_at DESC, job.requested_at DESC
         LIMIT 1;

        IF FOUND THEN
            NEW.pulse_ai_processing_status := 'failed';
            IF COALESCE(NEW.pulse_ai_processing_error_code, '') = '' THEN
                NEW.pulse_ai_processing_error_code := COALESCE(exhausted_diagnostic, 'retry_attempts_exhausted');
            END IF;
            NEW.pulse_ai_processing_updated_at := NOW();
        END IF;
    END IF;
    RETURN NEW;
END;
$projectpulse097_retry_document$;

DROP TRIGGER IF EXISTS trg_projectpulse097_document_retry_exhaustion
    ON project_intake_documents;
CREATE TRIGGER trg_projectpulse097_document_retry_exhaustion
BEFORE INSERT OR UPDATE OF pulse_ai_processing_status, pulse_ai_processing_error_code
ON project_intake_documents
FOR EACH ROW
EXECUTE FUNCTION projectpulse097_enforce_document_retry_exhaustion();

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '097_project_planning_identity_safe_admission',
    'Retire legacy identity-less project AI queueing and enforce bounded private-processing retry exhaustion',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
