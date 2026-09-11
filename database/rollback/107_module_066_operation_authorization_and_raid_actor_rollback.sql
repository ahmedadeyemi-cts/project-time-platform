BEGIN;
DELETE FROM app_role_permissions role_permission
USING app_roles role, app_permissions permission
WHERE role_permission.app_role_id = role.app_role_id
  AND role_permission.app_permission_id = permission.app_permission_id
  AND permission.permission_code IN ('MANAGE_FLOWHIVE_MEETINGS_066','MANAGE_FLOWHIVE_TASK_REMINDERS_066')
  AND role.role_code IN ('SUPER_ADMINISTRATOR','SYSTEM_ADMINISTRATOR','ADMINISTRATOR','PROJECT_MANAGER','PROJECT_MANAGEMENT','PROJECT_MANAGEMENT_LEAD','PROJECT_MANAGEMENT_TEAM_LEAD','PM_TEAM_LEAD');
DELETE FROM schema_migrations WHERE migration_id='107_module_066_operation_authorization_and_raid_actor';
COMMIT;
