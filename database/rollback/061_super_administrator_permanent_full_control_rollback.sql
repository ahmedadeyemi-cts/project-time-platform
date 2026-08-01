-- ProjectPulse rollback 061
-- Remove only authority relationships and canonical assignments introduced by
-- migration 061. Pre-existing Super Administrator assignments and permissions
-- are preserved.

BEGIN;

DROP TRIGGER IF EXISTS trg_role_access_repair_061_assignments_immutable
    ON role_access_repair_061_assignment_changes;
DROP TRIGGER IF EXISTS trg_role_access_repair_061_permissions_immutable
    ON role_access_repair_061_permission_changes;

DELETE FROM app_role_permissions relationship
USING role_access_repair_061_permission_changes change
WHERE relationship.app_role_id = change.role_id
  AND relationship.app_permission_id = change.permission_id;

DELETE FROM app_user_role_assignments assignment
USING role_access_repair_061_assignment_changes change
WHERE assignment.user_id = change.user_id
  AND assignment.app_role_id = change.target_role_id
  AND change.previous_assignment_existed = FALSE;

UPDATE app_user_role_assignments assignment
SET is_active = COALESCE(change.previous_is_active, FALSE),
    assignment_reason = change.previous_assignment_reason,
    updated_at = NOW()
FROM role_access_repair_061_assignment_changes change
WHERE assignment.user_id = change.user_id
  AND assignment.app_role_id = change.target_role_id
  AND change.previous_assignment_existed = TRUE;

DROP TABLE IF EXISTS role_access_repair_061_permission_changes;
DROP TABLE IF EXISTS role_access_repair_061_assignment_changes;
DROP FUNCTION IF EXISTS projectpulse_061_block_evidence_mutation();

DELETE FROM schema_migrations
WHERE migration_id = '061_super_administrator_permanent_full_control';

COMMIT;
