-- 019M-AV Approval Export Audit Workflow Hardening

INSERT INTO app_permissions (permission_code, permission_name, module_code, permission_description)
VALUES
    ('VIEW_WORKFLOW_OPERATIONAL_READINESS', 'View Workflow Operational Readiness', 'APPROVAL_WORKFLOW', 'View operational readiness, export readiness, and audit evidence for the approval/export/audit workflow.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
    ON p.permission_code = 'VIEW_WORKFLOW_OPERATIONAL_READINESS'
WHERE r.role_code IN (
    'ADMINISTRATOR',
    'PROJECT_TEAM_COORDINATOR',
    'PROJECT_MANAGEMENT',
    'PROJECT_MANAGER',
    'MANAGER',
    'EXECUTIVE',
    'ACCOUNTING'
)
ON CONFLICT DO NOTHING;

CREATE INDEX IF NOT EXISTS ix_time_entries_workflow_status_date
ON time_entries(status, work_date);

CREATE INDEX IF NOT EXISTS ix_audit_logs_workflow_evidence
ON audit_logs(action, entity_type, created_at DESC);
