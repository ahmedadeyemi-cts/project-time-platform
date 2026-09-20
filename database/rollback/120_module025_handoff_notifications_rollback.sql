-- Preserve immutable handoff and delivery history. Reapplying the migration
-- preserves these disabled preferences; an administrator explicitly re-enables.
BEGIN;
UPDATE enterprise_notification_policies SET enabled=FALSE,updated_at=NOW()
WHERE policy_code IN ('MODULE025_HANDOFF','MODULE025_COVERAGE_STARTED',
  'MODULE025_COVERAGE_RETURNED','MODULE025_HANDOFF_ACKNOWLEDGED');
COMMIT;
