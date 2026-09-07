-- Never erase execution evidence or revert to unbounded processing after use.
BEGIN;
DO $guard$
BEGIN
    IF EXISTS(SELECT 1 FROM project_flowhive_ai_planner_runs WHERE execution_contract<>'') THEN
        RAISE EXCEPTION 'Rollback refused: bounded FlowHive execution evidence exists. Roll forward instead.';
    END IF;
END;
$guard$;
DROP TRIGGER IF EXISTS trg_flowhive_104_execution_fence ON project_flowhive_ai_planner_runs;
DROP FUNCTION IF EXISTS projectpulse104_fence_planner_execution();
DROP INDEX IF EXISTS ix_flowhive_104_deadline;
ALTER TABLE project_flowhive_ai_planner_runs
    DROP COLUMN execution_contract, DROP COLUMN deadline_at, DROP COLUMN input_fingerprint,
    DROP COLUMN source_selection_fingerprint, DROP COLUMN source_version_fingerprint,
    DROP COLUMN expected_working_row_version, DROP COLUMN attempt_count, DROP COLUMN next_attempt_at,
    DROP COLUMN phase_started_at, DROP COLUMN retry_document_processing,
    DROP COLUMN saved_working_row_version, DROP COLUMN saved_working_revision;
DELETE FROM schema_migrations WHERE migration_id='104_flowhive_bounded_ai_execution';
COMMIT;
