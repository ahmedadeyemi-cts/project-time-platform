BEGIN;

DROP TABLE IF EXISTS project_flowhive_auto_plan_user_defaults;

ALTER TABLE project_flowhive_auto_plans
  DROP CONSTRAINT IF EXISTS project_flowhive_auto_plans_source_check;
ALTER TABLE project_flowhive_auto_plans
  ADD CONSTRAINT project_flowhive_auto_plans_source_check
  CHECK(source IN ('project_setting','new_project_default'));

DELETE FROM schema_migrations
WHERE migration_id='127_flowhive_pm_automatic_planning_defaults';

COMMIT;
