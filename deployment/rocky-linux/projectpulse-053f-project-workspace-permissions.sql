-- 053F - PM and Engineer Project Document Visibility
-- Purpose:
--   Ensure the legacy PROJECT_MANAGER role can open Project Workspace and view
--   project documents for assigned/managed projects, matching PROJECT_MANAGEMENT.

BEGIN;

DO $$
DECLARE
    missing_permissions text;
BEGIN
    SELECT string_agg(required.permission_code, ', ' ORDER BY required.permission_code)
    INTO missing_permissions
    FROM (
        VALUES
            ('VIEW_PROJECT_WORKSPACE'),
            ('VIEW_ENGINEERING_PROJECT_DOCUMENTS')
    ) AS required(permission_code)
    WHERE NOT EXISTS (
        SELECT 1
        FROM app_permissions p
        WHERE p.permission_code = required.permission_code
    );

    IF missing_permissions IS NOT NULL THEN
        RAISE EXCEPTION '053F required permission(s) missing from app_permissions: %', missing_permissions;
    END IF;
END
$$;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
  ON p.permission_code IN ('VIEW_PROJECT_WORKSPACE', 'VIEW_ENGINEERING_PROJECT_DOCUMENTS')
WHERE r.role_code IN ('PROJECT_MANAGER')
  AND NOT EXISTS (
      SELECT 1
      FROM app_role_permissions existing
      WHERE existing.app_role_id = r.app_role_id
        AND existing.app_permission_id = p.app_permission_id
  );

COMMIT;
