-- 019M-Q Project Workspace + Engineering Documents
-- Production-shaped bridge from intake documents to project/engineering workspace.

INSERT INTO app_permissions (permission_code, permission_name, module_code, permission_description)
VALUES
  ('VIEW_PROJECT_WORKSPACE', 'View project workspace', 'projects', 'View project workspace, project documents, assignments, and engineering readiness.'),
  ('MANAGE_PROJECT_DOCUMENTS', 'Manage project documents', 'projects', 'Upload, classify, and manage project documents such as SOW and GSD.'),
  ('VIEW_ENGINEERING_PROJECT_DOCUMENTS', 'View engineering project documents', 'projects', 'View engineering-visible project documents such as SOW, GSD, architecture, and support artifacts.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code IN (
    'VIEW_PROJECT_WORKSPACE',
    'MANAGE_PROJECT_DOCUMENTS',
    'VIEW_ENGINEERING_PROJECT_DOCUMENTS'
)
WHERE r.role_code IN ('ADMINISTRATOR', 'MANAGER', 'PROJECT_MANAGEMENT', 'PROJECT_TEAM_COORDINATOR')
  AND NOT EXISTS (
      SELECT 1
      FROM app_role_permissions existing
      WHERE existing.app_role_id = r.app_role_id
        AND existing.app_permission_id = p.app_permission_id
  );

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code IN (
    'VIEW_PROJECT_WORKSPACE',
    'VIEW_ENGINEERING_PROJECT_DOCUMENTS'
)
WHERE r.role_code IN ('ENGINEER', 'EXECUTIVE')
  AND NOT EXISTS (
      SELECT 1
      FROM app_role_permissions existing
      WHERE existing.app_role_id = r.app_role_id
        AND existing.app_permission_id = p.app_permission_id
  );

-- Attach any intake documents to a project when the intake already has resource requests linked to a project.
UPDATE project_intake_documents d
SET project_id = err.project_id
FROM engineering_resource_requests err
WHERE d.project_intake_request_id = err.project_intake_request_id
  AND d.project_id IS NULL
  AND err.project_id IS NOT NULL;

-- Existing project_allocation document files remain separate for now.
-- Come back item: unify project_document_files and project_intake_documents under a common project_document_artifacts view/table.
