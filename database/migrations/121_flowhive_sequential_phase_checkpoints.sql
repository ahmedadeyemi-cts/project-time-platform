BEGIN;
ALTER TABLE project_flowhive_ai_planner_runs
    ADD COLUMN IF NOT EXISTS phase_checkpoint JSONB NOT NULL DEFAULT '{}'::jsonb;
DO $body$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_flowhive_phase_checkpoint_object') THEN
        ALTER TABLE project_flowhive_ai_planner_runs ADD CONSTRAINT ck_flowhive_phase_checkpoint_object
            CHECK(jsonb_typeof(phase_checkpoint)='object');
    END IF;
END;
$body$;
-- Old in-flight batch runs must not resume under the sequential execution contract.
UPDATE project_flowhive_ai_planner_runs
   SET status='needs_attention',phase='execution_upgrade_required',completed_at=NOW(),updated_at=NOW(),
       blockers='["FlowHive now generates five sequential phases. Start a new run; existing plans and history are preserved."]'::jsonb
 WHERE status IN ('queued','processing','generating')
   AND execution_contract <> 'flowhive-sequential-execution-v2-20260920';
INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES('121_flowhive_sequential_phase_checkpoints','Private phase evidence, validated WBS checkpoints and durable phase timers',NOW())
ON CONFLICT(migration_id) DO NOTHING;
COMMIT;
