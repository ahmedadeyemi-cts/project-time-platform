-- 019M-AW / 019M-AX / 019M-AY Export package, dashboard registry, and audit evidence detail sprint

INSERT INTO app_permissions (permission_code, permission_name, module_code, permission_description)
VALUES
    ('DOWNLOAD_TIME_EXPORT_PACKAGE', 'Download Time Export Package', 'APPROVAL_WORKFLOW', 'Download generated time export packages for accounting and audit workflows.'),
    ('VIEW_WORKFLOW_AUDIT_EVIDENCE', 'View Workflow Audit Evidence', 'APPROVAL_WORKFLOW', 'View detailed approval, reconciliation, lock, and export audit evidence.'),
    ('VIEW_EXPORT_PACKAGE_READINESS', 'View Export Package Readiness', 'APPROVAL_WORKFLOW', 'View export package generation, download readiness, and file metadata.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
  ON p.permission_code IN (
      'DOWNLOAD_TIME_EXPORT_PACKAGE',
      'VIEW_WORKFLOW_AUDIT_EVIDENCE',
      'VIEW_EXPORT_PACKAGE_READINESS'
  )
WHERE r.role_code IN ('ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR', 'ACCOUNTING')
ON CONFLICT DO NOTHING;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
  ON p.permission_code = 'VIEW_WORKFLOW_AUDIT_EVIDENCE'
WHERE r.role_code IN ('PROJECT_MANAGEMENT', 'PROJECT_MANAGER', 'MANAGER', 'EXECUTIVE')
ON CONFLICT DO NOTHING;

ALTER TABLE time_workflow_exports
    ADD COLUMN IF NOT EXISTS package_generated_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS package_content_type character varying(128),
    ADD COLUMN IF NOT EXISTS package_file_extension character varying(16),
    ADD COLUMN IF NOT EXISTS package_download_count integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS package_last_downloaded_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS package_last_downloaded_by_user_id uuid;

UPDATE time_workflow_exports
SET package_generated_at = COALESCE(package_generated_at, created_at),
    package_content_type = COALESCE(package_content_type, 'text/csv'),
    package_file_extension = COALESCE(package_file_extension, 'csv')
WHERE package_generated_at IS NULL
   OR package_content_type IS NULL
   OR package_file_extension IS NULL;

CREATE INDEX IF NOT EXISTS ix_time_workflow_exports_package_download
ON time_workflow_exports(export_status, package_generated_at DESC, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_audit_logs_workflow_export_evidence
ON audit_logs(entity_type, entity_id, created_at DESC);
