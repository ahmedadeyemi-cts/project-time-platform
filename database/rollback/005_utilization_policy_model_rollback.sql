-- Project Time Platform
-- Rollback: 005_utilization_policy_model_rollback.sql
-- Purpose: Roll back utilization policy, targets, weekly summaries, and utilization buckets.

BEGIN;

DROP INDEX IF EXISTS idx_utilization_weekly_summaries_user_week;
DROP INDEX IF EXISTS idx_utilization_policy_targets_policy;
DROP INDEX IF EXISTS idx_utilization_policies_active;
DROP INDEX IF EXISTS idx_non_project_time_categories_utilization_bucket;
DROP INDEX IF EXISTS idx_project_tasks_utilization_bucket;

DROP TABLE IF EXISTS utilization_weekly_summaries;
DROP TABLE IF EXISTS utilization_policy_targets;
DROP TABLE IF EXISTS utilization_policies;

ALTER TABLE utilization_snapshots
    DROP COLUMN IF EXISTS bonus_reference_amount,
    DROP COLUMN IF EXISTS total_utilization_percent,
    DROP COLUMN IF EXISTS utilization_percent_without_presales,
    DROP COLUMN IF EXISTS total_utilization_hours,
    DROP COLUMN IF EXISTS presales_training_hours,
    DROP COLUMN IF EXISTS pto_hours,
    DROP COLUMN IF EXISTS afterhours_utilized_hours,
    DROP COLUMN IF EXISTS regular_utilized_hours,
    DROP COLUMN IF EXISTS standard_period_hours;

ALTER TABLE non_project_time_categories
    DROP CONSTRAINT IF EXISTS chk_non_project_time_utilization_bucket;

ALTER TABLE non_project_time_categories
    DROP COLUMN IF EXISTS utilization_bucket;

ALTER TABLE project_tasks
    DROP CONSTRAINT IF EXISTS chk_project_task_utilization_bucket;

ALTER TABLE project_tasks
    DROP COLUMN IF EXISTS utilization_requires_approval,
    DROP COLUMN IF EXISTS utilization_bucket;

DELETE FROM schema_migrations
WHERE migration_id = '005_utilization_policy_model';

COMMIT;
