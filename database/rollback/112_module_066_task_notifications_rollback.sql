-- Retain state and immutable delivery history; stop producing/delivering FlowHive task events.
BEGIN;
UPDATE enterprise_notification_policies SET enabled=FALSE, updated_at=NOW()
WHERE policy_code IN ('FLOWHIVE_TASK_ASSIGNED','FLOWHIVE_TASK_DUE');
COMMIT;
