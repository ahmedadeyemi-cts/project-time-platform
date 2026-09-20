-- SOW/GSD handoffs enqueue in the ownership transaction. Module 065 is the sole
-- transport authority; installing this policy never sends email or Teams messages.
BEGIN;
DO $$ BEGIN
  IF to_regclass('public.enterprise_notification_events') IS NULL
     OR to_regclass('public.enterprise_notification_event_history') IS NULL
     OR to_regclass('public.module025_sow_gsd_events') IS NULL THEN
    RAISE EXCEPTION 'SOW/GSD handoff notifications require migrations 064 and 099';
  END IF;
END $$;
CREATE INDEX IF NOT EXISTS ix_enterprise_module025_handoff_status
  ON enterprise_notification_events(entity_id,occurred_at DESC,created_at DESC)
  WHERE source_module='025' AND entity_type='module025_sow_gsd';
INSERT INTO enterprise_notification_policies (
  policy_code, policy_name, category, source_module, event_code, trigger_mode,
  recipient_strategy, delivery_boundary, subject_template, text_template, producer_contract, source_state)
SELECT policy_code, policy_name, 'project', '025', event_code, 'event',
  'module025_handoff', 'test_only', '[{{engagementNumber}}] {{notificationLabel}}',
  '{{notificationLabel}} for {{engagementNumber}}. Current owner: {{ownerDisplayName}}. Open the SOW/GSD workspace to review the handoff and its retained history: {{projectPulseUrl}}',
  'module025-handoff-v1', 'native_worker'
FROM (VALUES
  ('MODULE025_HANDOFF','SOW/GSD ownership handoff','sow_gsd.ownership_transferred'),
  ('MODULE025_COVERAGE_STARTED','SOW/GSD temporary coverage','sow_gsd.coverage_started'),
  ('MODULE025_COVERAGE_RETURNED','SOW/GSD coverage returned','sow_gsd.coverage_returned'),
  ('MODULE025_HANDOFF_ACKNOWLEDGED','SOW/GSD handoff acknowledged','sow_gsd.coverage_acknowledged')
) AS policies(policy_code, policy_name, event_code)
ON CONFLICT(policy_code) DO NOTHING;
INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES('120_module025_handoff_notifications','Transactional SOW/GSD handoff events through Module 065',NOW())
ON CONFLICT(migration_id) DO NOTHING;
COMMIT;
