-- Module 066 produces task lifecycle events; Module 065 alone delivers them.
BEGIN;
DO $$ BEGIN
  IF to_regclass('public.enterprise_notification_policies') IS NULL
     OR to_regclass('public.project_flowhive_task_reminder_preferences') IS NULL THEN
    RAISE EXCEPTION 'FlowHive task notifications require migrations 064 and 103';
  END IF;
END $$;
CREATE TABLE IF NOT EXISTS project_flowhive_notification_state (
  project_id UUID PRIMARY KEY REFERENCES projects(project_id) ON DELETE CASCADE,
  state JSONB NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(state) = 'object'),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
ALTER TABLE project_flowhive_task_reminder_preferences ALTER COLUMN lead_days SET DEFAULT ARRAY[3,0]::SMALLINT[];
-- Preserve all saved preferences, delivery boundaries, policies, and audit evidence on replay.
INSERT INTO enterprise_notification_policies (
  policy_code, policy_name, category, source_module, event_code, trigger_mode,
  recipient_strategy, delivery_boundary, subject_template, text_template, producer_contract, source_state)
VALUES
  ('FLOWHIVE_TASK_ASSIGNED','FlowHive task assignment','project','066','flowhive.task.assigned','event',
   'subject_user','test_only','[{{projectCode}}] Task assigned: {{taskName}}',
   'You have been assigned {{taskWbs}} — {{taskName}}. Due: {{dueDate}} ({{timezone}}). Open the project: {{projectPulseUrl}}',
   'flowhive-task-events-v1','scanner'),
  ('FLOWHIVE_TASK_DUE','FlowHive task due date','project','066','flowhive.task.due','scheduled',
   'subject_user','test_only','[{{projectCode}}] {{notificationLabel}}: {{taskName}}',
   '{{taskWbs}} — {{taskName}} is {{notificationLabel}}. Due: {{dueDate}} ({{timezone}}). Open the project: {{projectPulseUrl}}',
   'flowhive-task-events-v1','scanner')
ON CONFLICT(policy_code) DO NOTHING;
INSERT INTO schema_migrations(migration_id, description, applied_at)
VALUES ('115_module_066_task_notifications','FlowHive approved-WBS assignment and due-date events through Module 065',NOW())
ON CONFLICT(migration_id) DO NOTHING;
COMMIT;
