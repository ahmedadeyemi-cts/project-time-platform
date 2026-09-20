-- Retain checkpoints and audit history on application rollback. The additive column is backward-compatible.
-- The old worker does not claim the new execution contract; do not relabel or resume those runs.
SELECT COUNT(*) AS retained_sequential_runs FROM project_flowhive_ai_planner_runs
WHERE execution_contract='flowhive-sequential-execution-v2-20260920';
