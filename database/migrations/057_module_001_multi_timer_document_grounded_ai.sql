-- ProjectPulse Module 001 — simultaneous timers and document-grounded AI descriptions.
--
-- This migration is additive after Modules 041, 052, and 053. It:
--   * raises the authoritative timer safety cap from 12 to 24 hours;
--   * permits at most five distinct running activity timers per user;
--   * keeps one running timer per assignment or non-project activity;
--   * makes every active engineering-visible project document eligible for
--     permission-scoped AI generation, including Timesheet and service-request
--     descriptions; and
--   * queues eligible documents for the existing private document pipeline.
--
-- Raw document content remains inside the private, permission-aware ProjectPulse
-- retrieval boundary. This migration does not send document text to Claude or
-- OpenAI and does not change role or project authorization.

BEGIN;

DO $module001_057_prerequisites$
DECLARE
    required_migration TEXT;
BEGIN
    FOREACH required_migration IN ARRAY ARRAY[
        '041_module_001_timesheet_timer_and_task_association',
        '052_pulse_ai_private_document_runtime',
        '053_pulse_ai_private_rag_orchestration'
    ]
    LOOP
        IF NOT EXISTS (
            SELECT 1
            FROM schema_migrations
            WHERE migration_id = required_migration
        ) THEN
            RAISE EXCEPTION 'Migration 057 requires % first.', required_migration;
        END IF;
    END LOOP;
END;
$module001_057_prerequisites$;

-- Serialize the structural change with timer writers while the historic unique
-- index and 12-hour constraints are replaced.
LOCK TABLE module001_timer_sessions IN SHARE ROW EXCLUSIVE MODE;

DROP INDEX IF EXISTS ux_module001_one_running_timer_per_user;

ALTER TABLE module001_timer_sessions
    DROP CONSTRAINT IF EXISTS chk_module001_timer_actual_seconds,
    DROP CONSTRAINT IF EXISTS chk_module001_timer_rounded_minutes;

ALTER TABLE module001_timer_sessions
    ADD CONSTRAINT chk_module001_timer_actual_seconds
        CHECK (actual_elapsed_seconds IS NULL OR actual_elapsed_seconds BETWEEN 0 AND 86400),
    ADD CONSTRAINT chk_module001_timer_rounded_minutes
        CHECK (rounded_minutes IS NULL OR (rounded_minutes BETWEEN 0 AND 1440 AND rounded_minutes % 15 = 0));

ALTER TABLE module001_timer_daily_segments
    DROP CONSTRAINT IF EXISTS chk_module001_timer_segment_rounded;

ALTER TABLE module001_timer_daily_segments
    ADD CONSTRAINT chk_module001_timer_segment_rounded
        CHECK (allocated_rounded_minutes BETWEEN 0 AND 1440 AND allocated_rounded_minutes % 15 = 0);

-- A task may not be represented by two running timers for the same user. The
-- two partial indexes preserve that rule without restoring the old one-timer cap.
CREATE UNIQUE INDEX IF NOT EXISTS ux_module001_running_assignment
    ON module001_timer_sessions(user_id, assignment_id)
    WHERE timer_status = 'RUNNING' AND assignment_id IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_module001_running_non_project
    ON module001_timer_sessions(user_id, non_project_time_category_id)
    WHERE timer_status = 'RUNNING' AND non_project_time_category_id IS NOT NULL;

CREATE OR REPLACE FUNCTION module001_057_enforce_running_timer_limit()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $module001_057_running_timer_limit_body$
DECLARE
    active_count INTEGER;
BEGIN
    IF NEW.timer_status <> 'RUNNING' THEN
        RETURN NEW;
    END IF;

    -- The same key is also used by the API transaction. The database trigger is
    -- the final authority for concurrent requests from different app revisions.
    PERFORM pg_advisory_xact_lock(hashtextextended(NEW.user_id::TEXT, 57001));

    SELECT COUNT(*)::INTEGER
    INTO active_count
    FROM module001_timer_sessions timer
    WHERE timer.user_id = NEW.user_id
      AND timer.timer_status = 'RUNNING'
      AND timer.timer_session_id IS DISTINCT FROM NEW.timer_session_id;

    IF active_count >= 5 THEN
        RAISE EXCEPTION 'A maximum of five running timers is allowed per user.'
            USING ERRCODE = '23514',
                  CONSTRAINT = 'chk_module001_057_max_five_running_timers';
    END IF;

    RETURN NEW;
END;
$module001_057_running_timer_limit_body$;

DROP TRIGGER IF EXISTS trg_module001_057_running_timer_limit
    ON module001_timer_sessions;
CREATE TRIGGER trg_module001_057_running_timer_limit
BEFORE INSERT OR UPDATE OF user_id, timer_status
ON module001_timer_sessions
FOR EACH ROW
EXECUTE FUNCTION module001_057_enforce_running_timer_limit();

-- The legacy column name is retained for compatibility, but its policy now means
-- "eligible for permission-scoped project AI generation." engineering_visible is
-- still the primary content-visibility boundary.
ALTER TABLE project_intake_documents
    ALTER COLUMN ai_timesheet_context_enabled SET DEFAULT FALSE;

UPDATE project_intake_documents
SET ai_timesheet_context_enabled = TRUE
WHERE is_active = TRUE
  AND COALESCE(engineering_visible, FALSE) = TRUE
  AND ai_timesheet_context_enabled IS DISTINCT FROM TRUE;

UPDATE pulse_ai_document_chunks chunk
SET ai_timesheet_context_enabled = TRUE
WHERE chunk.is_active = TRUE
  AND EXISTS (
      SELECT 1
      FROM project_intake_documents document
      WHERE document.project_intake_document_id = chunk.project_intake_document_id
        AND document.is_active = TRUE
        AND COALESCE(document.engineering_visible, FALSE) = TRUE
        AND COALESCE(document.ai_timesheet_context_enabled, FALSE) = TRUE
  )
  AND chunk.ai_timesheet_context_enabled IS DISTINCT FROM TRUE;

CREATE OR REPLACE FUNCTION module001_057_normalize_project_ai_document_policy()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $module001_057_document_policy_body$
BEGIN
    IF NEW.is_active = TRUE
       AND COALESCE(NEW.engineering_visible, FALSE) = TRUE
    THEN
        NEW.ai_timesheet_context_enabled = TRUE;
    END IF;

    RETURN NEW;
END;
$module001_057_document_policy_body$;

DROP TRIGGER IF EXISTS trg_module001_057_project_ai_document_policy_insert
    ON project_intake_documents;
DROP TRIGGER IF EXISTS trg_module001_057_project_ai_document_policy_update
    ON project_intake_documents;
CREATE TRIGGER trg_module001_057_project_ai_document_policy_insert
BEFORE INSERT ON project_intake_documents
FOR EACH ROW
EXECUTE FUNCTION module001_057_normalize_project_ai_document_policy();
CREATE TRIGGER trg_module001_057_project_ai_document_policy_update
BEFORE UPDATE OF is_active, engineering_visible, ai_timesheet_context_enabled, project_id
ON project_intake_documents
FOR EACH ROW
EXECUTE FUNCTION module001_057_normalize_project_ai_document_policy();

CREATE OR REPLACE FUNCTION module001_057_propagate_project_ai_document_policy()
RETURNS TRIGGER
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $module001_057_document_propagation_body$
BEGIN
    UPDATE pulse_ai_document_chunks
    SET ai_timesheet_context_enabled = (
            NEW.is_active = TRUE
            AND COALESCE(NEW.engineering_visible, FALSE) = TRUE
            AND COALESCE(NEW.ai_timesheet_context_enabled, FALSE) = TRUE
        )
    WHERE project_intake_document_id = NEW.project_intake_document_id
      AND ai_timesheet_context_enabled IS DISTINCT FROM (
            NEW.is_active = TRUE
            AND COALESCE(NEW.engineering_visible, FALSE) = TRUE
            AND COALESCE(NEW.ai_timesheet_context_enabled, FALSE) = TRUE
        );

    RETURN NEW;
END;
$module001_057_document_propagation_body$;

DROP TRIGGER IF EXISTS trg_module001_057_propagate_project_ai_document_policy_insert
    ON project_intake_documents;
DROP TRIGGER IF EXISTS trg_module001_057_propagate_project_ai_document_policy_update
    ON project_intake_documents;
CREATE TRIGGER trg_module001_057_propagate_project_ai_document_policy_insert
AFTER INSERT ON project_intake_documents
FOR EACH ROW
EXECUTE FUNCTION module001_057_propagate_project_ai_document_policy();
CREATE TRIGGER trg_module001_057_propagate_project_ai_document_policy_update
AFTER UPDATE OF is_active, engineering_visible, ai_timesheet_context_enabled, project_id
ON project_intake_documents
FOR EACH ROW
EXECUTE FUNCTION module001_057_propagate_project_ai_document_policy();

CREATE OR REPLACE FUNCTION module001_057_queue_project_ai_document()
RETURNS TRIGGER
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $module001_057_document_queue_body$
DECLARE
    queued_job_id UUID;
    queue_priority SMALLINT;
BEGIN
    IF NEW.project_id IS NULL
       OR NEW.is_active <> TRUE
       OR COALESCE(NEW.engineering_visible, FALSE) <> TRUE
       OR COALESCE(NEW.ai_timesheet_context_enabled, FALSE) <> TRUE
       OR (
            COALESCE(NEW.pulse_ai_processing_status, 'not_requested') = 'ready'
            AND NEW.pulse_ai_active_version_id IS NOT NULL
       )
    THEN
        RETURN NEW;
    END IF;

    queue_priority := CASE LOWER(COALESCE(NEW.document_category, NEW.document_type, 'other'))
        WHEN 'sow' THEN 100
        WHEN 'statement_of_work' THEN 100
        WHEN 'gsd' THEN 95
        WHEN 'global_solution_design' THEN 95
        WHEN 'architecture' THEN 90
        WHEN 'design' THEN 90
        WHEN 'order' THEN 85
        WHEN 'order_form' THEN 85
        WHEN 'quote' THEN 80
        WHEN 'proposal' THEN 80
        ELSE 70
    END;

    INSERT INTO pulse_ai_document_processing_jobs (
        project_intake_document_id,
        project_id,
        requested_by_user_id,
        requested_purpose,
        priority,
        job_status,
        correlation_id
    )
    VALUES (
        NEW.project_intake_document_id,
        NEW.project_id,
        NEW.uploaded_by_user_id,
        'project_ai_generation_grounding',
        queue_priority,
        'queued',
        CONCAT('module001-057-', NEW.project_intake_document_id::TEXT)
    )
    ON CONFLICT DO NOTHING
    RETURNING pulse_ai_document_processing_job_id INTO queued_job_id;

    IF queued_job_id IS NOT NULL THEN
        INSERT INTO pulse_ai_document_processing_events (
            pulse_ai_document_processing_job_id,
            project_intake_document_id,
            project_id,
            actual_user_id,
            effective_user_id,
            event_code,
            event_status,
            correlation_id,
            evidence_json
        )
        VALUES (
            queued_job_id,
            NEW.project_intake_document_id,
            NEW.project_id,
            NEW.uploaded_by_user_id,
            NEW.uploaded_by_user_id,
            'PROJECT_AI_CONTEXT_AUTO_QUEUED',
            'requested',
            CONCAT('module001-057-', NEW.project_intake_document_id::TEXT),
            jsonb_build_object(
                'policyVersion', 'project-ai-document-grounding-v1',
                'documentCategory', LOWER(COALESCE(NEW.document_category, NEW.document_type, 'other')),
                'engineeringVisible', TRUE,
                'permissionScopedRetrieval', TRUE,
                'rawDocumentSentToExternalProvider', FALSE
            )
        );
    END IF;

    RETURN NEW;
END;
$module001_057_document_queue_body$;

DROP TRIGGER IF EXISTS trg_module001_057_queue_project_ai_document_insert
    ON project_intake_documents;
DROP TRIGGER IF EXISTS trg_module001_057_queue_project_ai_document_update
    ON project_intake_documents;
CREATE TRIGGER trg_module001_057_queue_project_ai_document_insert
AFTER INSERT ON project_intake_documents
FOR EACH ROW
EXECUTE FUNCTION module001_057_queue_project_ai_document();
CREATE TRIGGER trg_module001_057_queue_project_ai_document_update
AFTER UPDATE OF project_id, is_active, engineering_visible, ai_timesheet_context_enabled
ON project_intake_documents
FOR EACH ROW
EXECUTE FUNCTION module001_057_queue_project_ai_document();

-- Queue existing eligible documents that do not already have a ready active
-- version or an active processing job. SOW and GSD receive the highest priority.
WITH queued AS (
    INSERT INTO pulse_ai_document_processing_jobs (
        project_intake_document_id,
        project_id,
        requested_by_user_id,
        requested_purpose,
        priority,
        job_status,
        correlation_id
    )
    SELECT
        document.project_intake_document_id,
        document.project_id,
        document.uploaded_by_user_id,
        'project_ai_generation_grounding',
        CASE LOWER(COALESCE(document.document_category, document.document_type, 'other'))
            WHEN 'sow' THEN 100
            WHEN 'statement_of_work' THEN 100
            WHEN 'gsd' THEN 95
            WHEN 'global_solution_design' THEN 95
            WHEN 'architecture' THEN 90
            WHEN 'design' THEN 90
            WHEN 'order' THEN 85
            WHEN 'order_form' THEN 85
            WHEN 'quote' THEN 80
            WHEN 'proposal' THEN 80
            ELSE 70
        END,
        'queued',
        CONCAT('module001-057-', document.project_intake_document_id::TEXT)
    FROM project_intake_documents document
    WHERE document.project_id IS NOT NULL
      AND document.is_active = TRUE
      AND COALESCE(document.engineering_visible, FALSE) = TRUE
      AND COALESCE(document.ai_timesheet_context_enabled, FALSE) = TRUE
      AND NOT (
          COALESCE(document.pulse_ai_processing_status, 'not_requested') = 'ready'
          AND document.pulse_ai_active_version_id IS NOT NULL
      )
      AND NOT EXISTS (
          SELECT 1
          FROM pulse_ai_document_processing_jobs active_job
          WHERE active_job.project_intake_document_id = document.project_intake_document_id
            AND active_job.job_status IN (
                'queued', 'scanning', 'extracting', 'awaiting_ocr',
                'embedding', 'indexing', 'retry_wait', 'cancel_requested'
            )
      )
    ON CONFLICT DO NOTHING
    RETURNING
        pulse_ai_document_processing_job_id,
        project_intake_document_id,
        project_id,
        requested_by_user_id,
        correlation_id
)
INSERT INTO pulse_ai_document_processing_events (
    pulse_ai_document_processing_job_id,
    project_intake_document_id,
    project_id,
    actual_user_id,
    effective_user_id,
    event_code,
    event_status,
    correlation_id,
    evidence_json
)
SELECT
    queued.pulse_ai_document_processing_job_id,
    queued.project_intake_document_id,
    queued.project_id,
    queued.requested_by_user_id,
    queued.requested_by_user_id,
    'PROJECT_AI_CONTEXT_AUTO_QUEUED',
    'requested',
    queued.correlation_id,
    jsonb_build_object(
        'policyVersion', 'project-ai-document-grounding-v1',
        'backfill', TRUE,
        'engineeringVisible', TRUE,
        'permissionScopedRetrieval', TRUE,
        'rawDocumentSentToExternalProvider', FALSE
    )
FROM queued;

REVOKE ALL ON FUNCTION module001_057_propagate_project_ai_document_policy() FROM PUBLIC;
REVOKE ALL ON FUNCTION module001_057_queue_project_ai_document() FROM PUBLIC;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '057_module_001_multi_timer_document_grounded_ai',
    'Enable up to five simultaneous 24-hour Module 001 timers and permission-scoped project-document grounding for AI generation',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
