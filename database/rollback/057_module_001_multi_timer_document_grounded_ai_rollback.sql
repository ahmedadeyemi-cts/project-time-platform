-- Guarded rollback for migration 057.
--
-- The rollback intentionally fails closed after multi-timer operational evidence,
-- 24-hour timer data, or project-document AI policy activity exists. It must not
-- silently collapse multiple running timers or remove document grounding that has
-- already produced immutable processing evidence.

BEGIN;

DO $module001_057_rollback_guard$
DECLARE
    multi_timer_user_count BIGINT;
    over_legacy_timer_count BIGINT;
    over_legacy_segment_count BIGINT;
    policy_job_count BIGINT;
    policy_event_count BIGINT;
    eligible_document_count BIGINT;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM schema_migrations
        WHERE migration_id = '057_module_001_multi_timer_document_grounded_ai'
    ) THEN
        RAISE EXCEPTION 'Migration 057 is not registered and cannot be rolled back.';
    END IF;

    SELECT COUNT(*) INTO multi_timer_user_count
    FROM (
        SELECT user_id
        FROM module001_timer_sessions
        WHERE timer_status = 'RUNNING'
        GROUP BY user_id
        HAVING COUNT(*) > 1
    ) users_with_multiple_timers;

    SELECT COUNT(*) INTO over_legacy_timer_count
    FROM module001_timer_sessions
    WHERE COALESCE(actual_elapsed_seconds, 0) > 43200
       OR COALESCE(rounded_minutes, 0) > 720;

    SELECT COUNT(*) INTO over_legacy_segment_count
    FROM module001_timer_daily_segments
    WHERE allocated_rounded_minutes > 720;

    SELECT COUNT(*) INTO policy_job_count
    FROM pulse_ai_document_processing_jobs
    WHERE requested_purpose = 'project_ai_generation_grounding';

    SELECT COUNT(*) INTO policy_event_count
    FROM pulse_ai_document_processing_events
    WHERE event_code = 'PROJECT_AI_CONTEXT_AUTO_QUEUED';

    SELECT COUNT(*) INTO eligible_document_count
    FROM project_intake_documents
    WHERE is_active = TRUE
      AND COALESCE(engineering_visible, FALSE) = TRUE;

    IF multi_timer_user_count
       + over_legacy_timer_count
       + over_legacy_segment_count
       + policy_job_count
       + policy_event_count
       + eligible_document_count > 0
    THEN
        RAISE EXCEPTION
            'Migration 057 rollback blocked: multi_timer_user=% over_12h_timer=% over_12h_segment=% project_ai_job=% immutable_project_ai_event=% eligible_project_document=%. Preserve the forward migration or complete an explicitly reviewed data-conversion plan.',
            multi_timer_user_count,
            over_legacy_timer_count,
            over_legacy_segment_count,
            policy_job_count,
            policy_event_count,
            eligible_document_count;
    END IF;
END;
$module001_057_rollback_guard$;

LOCK TABLE module001_timer_sessions IN SHARE ROW EXCLUSIVE MODE;

DROP TRIGGER IF EXISTS trg_module001_057_queue_project_ai_document_update
    ON project_intake_documents;
DROP TRIGGER IF EXISTS trg_module001_057_queue_project_ai_document_insert
    ON project_intake_documents;
DROP TRIGGER IF EXISTS trg_module001_057_propagate_project_ai_document_policy_update
    ON project_intake_documents;
DROP TRIGGER IF EXISTS trg_module001_057_propagate_project_ai_document_policy_insert
    ON project_intake_documents;
DROP TRIGGER IF EXISTS trg_module001_057_project_ai_document_policy_update
    ON project_intake_documents;
DROP TRIGGER IF EXISTS trg_module001_057_project_ai_document_policy_insert
    ON project_intake_documents;
DROP TRIGGER IF EXISTS trg_module001_057_running_timer_limit
    ON module001_timer_sessions;

DROP FUNCTION IF EXISTS module001_057_queue_project_ai_document();
DROP FUNCTION IF EXISTS module001_057_propagate_project_ai_document_policy();
DROP FUNCTION IF EXISTS module001_057_normalize_project_ai_document_policy();
DROP FUNCTION IF EXISTS module001_057_enforce_running_timer_limit();

DROP INDEX IF EXISTS ux_module001_running_assignment;
DROP INDEX IF EXISTS ux_module001_running_non_project;

ALTER TABLE module001_timer_sessions
    DROP CONSTRAINT IF EXISTS chk_module001_timer_actual_seconds,
    DROP CONSTRAINT IF EXISTS chk_module001_timer_rounded_minutes;

ALTER TABLE module001_timer_sessions
    ADD CONSTRAINT chk_module001_timer_actual_seconds
        CHECK (actual_elapsed_seconds IS NULL OR actual_elapsed_seconds BETWEEN 0 AND 43200),
    ADD CONSTRAINT chk_module001_timer_rounded_minutes
        CHECK (rounded_minutes IS NULL OR (rounded_minutes BETWEEN 0 AND 720 AND rounded_minutes % 15 = 0));

ALTER TABLE module001_timer_daily_segments
    DROP CONSTRAINT IF EXISTS chk_module001_timer_segment_rounded;
ALTER TABLE module001_timer_daily_segments
    ADD CONSTRAINT chk_module001_timer_segment_rounded
        CHECK (allocated_rounded_minutes BETWEEN 0 AND 720 AND allocated_rounded_minutes % 15 = 0);

CREATE UNIQUE INDEX ux_module001_one_running_timer_per_user
    ON module001_timer_sessions(user_id)
    WHERE timer_status = 'RUNNING';

ALTER TABLE project_intake_documents
    ALTER COLUMN ai_timesheet_context_enabled SET DEFAULT FALSE;

DELETE FROM schema_migrations
WHERE migration_id = '057_module_001_multi_timer_document_grounded_ai';

COMMIT;
