BEGIN;

DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id='122_flowhive_automatic_first_draft') THEN
    RAISE EXCEPTION 'FlowHive PM automatic planning defaults require migration 122';
  END IF;
END $$;

CREATE TABLE IF NOT EXISTS project_flowhive_auto_plan_user_defaults (
  user_id UUID PRIMARY KEY REFERENCES app_users(user_id),
  enabled BOOLEAN NOT NULL DEFAULT FALSE,
  applies_after TIMESTAMPTZ,
  row_version UUID NOT NULL DEFAULT gen_random_uuid(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CHECK(NOT enabled OR applies_after IS NOT NULL)
);

ALTER TABLE project_flowhive_auto_plans
  DROP CONSTRAINT IF EXISTS project_flowhive_auto_plans_source_check;
ALTER TABLE project_flowhive_auto_plans
  ADD CONSTRAINT project_flowhive_auto_plans_source_check
  CHECK(source IN ('project_setting','new_project_default','pm_default'));

INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES(
  '127_flowhive_pm_automatic_planning_defaults',
  'Add per-PM prospective automatic FlowHive planning defaults without changing organization-wide administration',
  NOW()
)
ON CONFLICT(migration_id) DO NOTHING;

COMMIT;
