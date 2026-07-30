-- Reviewed rollback for migration 056_role_workspace_entra_crm_governance.
-- Restores only role-permission relationships explicitly changed by migration
-- 056, then removes the migration-owned non-secret governance/evidence objects.

BEGIN;

DO $restore_role_permissions$
BEGIN
    IF to_regclass('public.role_workspace_permission_changes_056') IS NOT NULL THEN
        INSERT INTO app_role_permissions (app_role_id, app_permission_id)
        SELECT role.app_role_id, permission.app_permission_id
        FROM role_workspace_permission_changes_056 change
        JOIN app_roles role
          ON upper(role.role_code) = upper(change.role_code)
        JOIN app_permissions permission
          ON permission.permission_code = change.permission_code
        WHERE change.change_kind = 'removed'
        ON CONFLICT DO NOTHING;

        DELETE FROM app_role_permissions relationship
        USING role_workspace_permission_changes_056 change,
              app_roles role,
              app_permissions permission
        WHERE change.change_kind = 'granted'
          AND upper(role.role_code) = upper(change.role_code)
          AND permission.permission_code = change.permission_code
          AND relationship.app_role_id = role.app_role_id
          AND relationship.app_permission_id = permission.app_permission_id;
    END IF;
END;
$restore_role_permissions$;

DELETE FROM app_feature_catalog
WHERE feature_code IN (
    'ENTRA_SECRET_EXPIRATION_GOVERNANCE',
    'CRM_ERP_OAUTH_PERSISTENCE'
);

-- These three permission definitions are owned entirely by migration 056.
-- Remove any remaining relationship (including the Super Administrator
-- full-control invariant grant) before dropping their catalog rows.
DELETE FROM app_role_permissions relationship
USING app_permissions permission
WHERE relationship.app_permission_id = permission.app_permission_id
  AND permission.permission_code IN (
      'VIEW_ENTRA_SECRET_EXPIRATION',
      'MANAGE_ENTRA_SECRET_EXPIRATION',
      'ACKNOWLEDGE_ENTRA_SECRET_EXPIRATION'
  );

DROP TRIGGER IF EXISTS trg_role_workspace_permission_changes_056_immutable
    ON role_workspace_permission_changes_056;
DROP TRIGGER IF EXISTS trg_crm_integration_token_refresh_events_immutable
    ON crm_integration_token_refresh_events;
DROP TRIGGER IF EXISTS trg_entra_secret_expiration_audit_events_immutable
    ON entra_secret_expiration_audit_events;
DROP TRIGGER IF EXISTS trg_entra_secret_expiration_reminder_events_immutable
    ON entra_secret_expiration_reminder_events;
DROP TRIGGER IF EXISTS trg_entra_secret_expiration_acknowledgements_immutable
    ON entra_secret_expiration_acknowledgements;
DROP TRIGGER IF EXISTS trg_entra_secret_expiration_recipients_immutable
    ON entra_secret_expiration_recipients;
DROP TRIGGER IF EXISTS trg_entra_secret_expiration_profiles_immutable
    ON entra_secret_expiration_profile_versions;

DROP TABLE IF EXISTS entra_secret_expiration_audit_events;
DROP TABLE IF EXISTS entra_secret_expiration_reminder_events;
DROP TABLE IF EXISTS entra_secret_expiration_reminder_claims;
DROP TABLE IF EXISTS entra_secret_expiration_acknowledgements;
DROP TABLE IF EXISTS entra_secret_expiration_recipients;
DROP TABLE IF EXISTS entra_secret_expiration_state;
DROP TABLE IF EXISTS entra_secret_expiration_profile_versions;
DROP TABLE IF EXISTS crm_integration_token_refresh_events;
DROP TABLE IF EXISTS role_workspace_permission_changes_056;

DROP FUNCTION IF EXISTS projectpulse_056_block_immutable_mutation();

DELETE FROM app_permissions
WHERE permission_code IN (
    'VIEW_ENTRA_SECRET_EXPIRATION',
    'MANAGE_ENTRA_SECRET_EXPIRATION',
    'ACKNOWLEDGE_ENTRA_SECRET_EXPIRATION'
);

DELETE FROM schema_migrations
WHERE migration_id = '056_role_workspace_entra_crm_governance';

COMMIT;
