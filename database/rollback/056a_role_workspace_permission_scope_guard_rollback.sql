-- Reviewed rollback for migration 056A.
-- Restores only the migration-056 grants removed by the 056A least-privilege
-- guard. Roll back 056A before rolling back migration 056.

BEGIN;

DO $restore_scope_changes$
BEGIN
    IF to_regclass('public.role_workspace_permission_scope_changes_056a') IS NOT NULL THEN
        INSERT INTO app_role_permissions (app_role_id, app_permission_id)
        SELECT role.app_role_id, permission.app_permission_id
        FROM role_workspace_permission_scope_changes_056a change
        JOIN app_roles role
          ON upper(role.role_code) = upper(change.role_code)
        JOIN app_permissions permission
          ON permission.permission_code = change.permission_code
        ON CONFLICT DO NOTHING;
    END IF;
END;
$restore_scope_changes$;

DROP TRIGGER IF EXISTS trg_role_workspace_permission_scope_056a_immutable
    ON role_workspace_permission_scope_changes_056a;
DROP TABLE IF EXISTS role_workspace_permission_scope_changes_056a;
DROP FUNCTION IF EXISTS projectpulse_056a_block_immutable_mutation();

DELETE FROM schema_migrations
WHERE migration_id = '056a_role_workspace_permission_scope_guard';

COMMIT;
