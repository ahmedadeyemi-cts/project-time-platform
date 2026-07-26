BEGIN;

DELETE FROM app_role_permissions
WHERE app_permission_id=(SELECT app_permission_id FROM app_permissions WHERE permission_code='IMPORT_PROJECT_EXPENSE_CERTIFY')
  AND app_role_id IN (
      SELECT app_role_id FROM app_roles
      WHERE upper(role_code) IN ('ENGINEER','ENGINEERING','ENGINEERING_LEAD','ENGINEERING_TEAM_LEAD')
  );

DELETE FROM schema_migrations
WHERE migration_id='044a_project_expense_self_certify_permission';

COMMIT;
