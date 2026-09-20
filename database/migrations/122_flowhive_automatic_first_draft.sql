-- Explicit PM/admin consent queues only the first draft; no existing project is opted in by this migration.
BEGIN;
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id='121_flowhive_sequential_phase_checkpoints') THEN
    RAISE EXCEPTION 'Automatic FlowHive planning requires migration 121';
  END IF;
END $$;
CREATE TABLE IF NOT EXISTS project_flowhive_auto_plan_defaults (
  singleton BOOLEAN PRIMARY KEY DEFAULT TRUE CHECK(singleton),
  enabled BOOLEAN NOT NULL DEFAULT FALSE,
  authorized_by_user_id UUID REFERENCES app_users(user_id),
  applies_after TIMESTAMPTZ,
  row_version UUID NOT NULL DEFAULT gen_random_uuid(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CHECK(NOT enabled OR (authorized_by_user_id IS NOT NULL AND applies_after IS NOT NULL))
);
INSERT INTO project_flowhive_auto_plan_defaults(singleton) VALUES(TRUE) ON CONFLICT DO NOTHING;
CREATE TABLE IF NOT EXISTS project_flowhive_auto_plans (
  project_id UUID PRIMARY KEY REFERENCES projects(project_id),
  enabled BOOLEAN NOT NULL,
  authorized_by_user_id UUID NOT NULL REFERENCES app_users(user_id),
  source TEXT NOT NULL CHECK(source IN ('project_setting','new_project_default')),
  status TEXT NOT NULL DEFAULT 'waiting_documents',
  run_id UUID UNIQUE REFERENCES project_flowhive_ai_planner_runs(run_id),
  row_version UUID NOT NULL DEFAULT gen_random_uuid(),
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  checked_at TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS ix_flowhive_auto_plan_pending ON project_flowhive_auto_plans(checked_at,project_id)
  WHERE enabled=TRUE AND run_id IS NULL;
CREATE TABLE IF NOT EXISTS project_flowhive_auto_plan_events (
  event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  project_id UUID REFERENCES projects(project_id),
  actor_user_id UUID NOT NULL REFERENCES app_users(user_id),
  event_code TEXT NOT NULL,
  enabled BOOLEAN NOT NULL,
  occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
-- Module 065 continues to own delivery and its configured Test/production boundary.
INSERT INTO enterprise_notification_policies (
  policy_code,policy_name,category,source_module,event_code,trigger_mode,recipient_strategy,
  delivery_boundary,subject_template,text_template,producer_contract,source_state)
VALUES('FLOWHIVE_FIRST_DRAFT_READY','FlowHive first AI plan ready','project','066','flowhive.first_draft_ready',
  'event','subject_user','test_only','[{{projectCode}}] AI project plan ready for review',
  'The first AI project plan for {{projectCode}} is ready. Review its tasks, estimates and schedule in FlowHive before approving a baseline: {{projectPulseUrl}}',
  'flowhive-first-draft-v1','native_worker')
ON CONFLICT(policy_code) DO NOTHING;
INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES('122_flowhive_automatic_first_draft','Explicit project automation and prospective defaults for one reviewed FlowHive draft',NOW())
ON CONFLICT(migration_id) DO NOTHING;
COMMIT;
