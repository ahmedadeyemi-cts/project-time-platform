-- Bounded, cancelable planner execution. No canonical tasks or customer data are changed.
BEGIN;
DO $preconditions$
BEGIN
    IF to_regclass('public.project_flowhive_ai_planner_runs') IS NULL
       OR to_regclass('public.project_flowhive_working_copies') IS NULL THEN
        RAISE EXCEPTION 'Migration 104 requires FlowHive migrations 086 and 095.';
    END IF;
END;
$preconditions$;

ALTER TABLE project_flowhive_ai_planner_runs
    ADD COLUMN IF NOT EXISTS execution_contract VARCHAR(100) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS deadline_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS input_fingerprint VARCHAR(64) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS source_selection_fingerprint VARCHAR(64) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS source_version_fingerprint VARCHAR(64) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS expected_working_row_version UUID NULL,
    ADD COLUMN IF NOT EXISTS attempt_count SMALLINT NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS next_attempt_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    ADD COLUMN IF NOT EXISTS phase_started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    ADD COLUMN IF NOT EXISTS retry_document_processing BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS saved_working_row_version UUID NULL,
    ADD COLUMN IF NOT EXISTS saved_working_revision INTEGER NULL;

-- Runs created under the obsolete execution contract cannot overwrite a new working copy.
UPDATE project_flowhive_ai_planner_runs
SET status='needs_attention', phase='execution_upgrade_required', completed_at=NOW(), updated_at=NOW(),
    blockers='["Planner execution was upgraded. Existing work is preserved; start a new bounded run."]'::jsonb
WHERE status IN ('queued','processing','generating') AND execution_contract='';

CREATE INDEX IF NOT EXISTS ix_flowhive_104_deadline
    ON project_flowhive_ai_planner_runs(deadline_at)
    WHERE status IN ('queued','processing','generating');

CREATE OR REPLACE FUNCTION projectpulse104_fence_planner_execution()
RETURNS TRIGGER LANGUAGE plpgsql AS $body$
BEGIN
    IF OLD.completed_at IS NOT NULL AND NEW.status IN ('queued','processing','generating') THEN
        RAISE EXCEPTION 'A terminal planner run cannot be restarted.';
    END IF;
    IF OLD.deadline_at IS NOT NULL AND NEW.deadline_at IS DISTINCT FROM OLD.deadline_at THEN
        RAISE EXCEPTION 'A planner execution deadline cannot be extended.';
    END IF;
    IF OLD.execution_contract<>'' AND (
        NEW.execution_contract IS DISTINCT FROM OLD.execution_contract
        OR NEW.requested_outcome IS DISTINCT FROM OLD.requested_outcome
        OR NEW.detail_level IS DISTINCT FROM OLD.detail_level
        OR NEW.input_fingerprint IS DISTINCT FROM OLD.input_fingerprint
        OR NEW.source_selection_fingerprint IS DISTINCT FROM OLD.source_selection_fingerprint
        OR NEW.expected_working_row_version IS DISTINCT FROM OLD.expected_working_row_version
        OR NEW.requested_plan IS DISTINCT FROM OLD.requested_plan
        OR NEW.actual_actor_user_id IS DISTINCT FROM OLD.actual_actor_user_id
        OR NEW.effective_actor_user_id IS DISTINCT FROM OLD.effective_actor_user_id
        OR NEW.project_id IS DISTINCT FROM OLD.project_id) THEN
        RAISE EXCEPTION 'Planner execution inputs and starting revision are immutable.';
    END IF;
    IF OLD.source_version_fingerprint<>'' AND NEW.source_version_fingerprint<>OLD.source_version_fingerprint THEN
        RAISE EXCEPTION 'Planner source versions are pinned for the entire run.';
    END IF;
    IF OLD.saved_working_row_version IS NOT NULL AND (
        NEW.saved_working_row_version IS DISTINCT FROM OLD.saved_working_row_version
        OR NEW.saved_working_revision IS DISTINCT FROM OLD.saved_working_revision) THEN
        RAISE EXCEPTION 'A committed working-copy receipt cannot be replaced.';
    END IF;
    IF NEW.attempt_count<OLD.attempt_count OR NEW.attempt_count>2 THEN
        RAISE EXCEPTION 'Planner attempt budget cannot be reset or exceeded.';
    END IF;
    IF NEW.phase IS DISTINCT FROM OLD.phase THEN NEW.phase_started_at:=clock_timestamp(); END IF;
    RETURN NEW;
END;
$body$;
DROP TRIGGER IF EXISTS trg_flowhive_104_execution_fence ON project_flowhive_ai_planner_runs;
CREATE TRIGGER trg_flowhive_104_execution_fence BEFORE UPDATE ON project_flowhive_ai_planner_runs
FOR EACH ROW EXECUTE FUNCTION projectpulse104_fence_planner_execution();

INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES('104_flowhive_bounded_ai_execution','Bounded planner deadlines, cancellation fences, input/source identity and optimistic working-copy concurrency',NOW())
ON CONFLICT(migration_id) DO NOTHING;
COMMIT;
