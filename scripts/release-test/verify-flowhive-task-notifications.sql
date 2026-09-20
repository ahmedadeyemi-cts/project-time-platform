-- Read-only Protected Test verification. Never enable a policy or widen delivery.
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM schema_migrations
                 WHERE migration_id='115_module_066_task_notifications')
     OR to_regclass('public.project_flowhive_notification_state') IS NULL
     OR to_regclass('public.project_flowhive_task_reminder_preferences') IS NULL
     OR to_regclass('public.enterprise_notification_policies') IS NULL THEN
    RAISE EXCEPTION 'FlowHive notification migration 115 is incomplete';
  END IF;
  IF (SELECT count(*) FROM enterprise_notification_policies
       WHERE source_module='066' AND producer_contract='flowhive-task-events-v1'
         AND source_state='scanner' AND recipient_strategy='subject_user'
         AND delivery_boundary IN ('test_only','locked')
         AND ((policy_code='FLOWHIVE_TASK_ASSIGNED' AND event_code='flowhive.task.assigned' AND trigger_mode='event')
           OR (policy_code='FLOWHIVE_TASK_DUE' AND event_code='flowhive.task.due' AND trigger_mode='scheduled'))) <> 2 THEN
    RAISE EXCEPTION 'FlowHive task policies are missing or outside Protected Test delivery boundaries';
  END IF;
END $$;
