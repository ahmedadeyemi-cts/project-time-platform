BEGIN;

CREATE TABLE IF NOT EXISTS project_allocation_projects (
    project_allocation_project_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_code TEXT NOT NULL UNIQUE,
    project_name TEXT NOT NULL,
    customer_name TEXT NULL,
    service_request_number TEXT NULL,
    project_status TEXT NOT NULL DEFAULT 'intake',
    created_by_user_id UUID REFERENCES app_users(user_id),
    updated_by_user_id UUID REFERENCES app_users(user_id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS project_engineer_allocations (
    project_engineer_allocation_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_allocation_project_id UUID NOT NULL REFERENCES project_allocation_projects(project_allocation_project_id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    allocated_hours NUMERIC(10,2) NOT NULL DEFAULT 0,
    allocation_notes TEXT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    allocated_by_user_id UUID REFERENCES app_users(user_id),
    allocated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(project_allocation_project_id, user_id)
);

CREATE TABLE IF NOT EXISTS project_document_files (
    project_document_file_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_allocation_project_id UUID NOT NULL REFERENCES project_allocation_projects(project_allocation_project_id) ON DELETE CASCADE,
    document_type TEXT NOT NULL CHECK (document_type IN ('SOW', 'GSD')),
    original_file_name TEXT NOT NULL,
    stored_file_name TEXT NOT NULL,
    storage_path TEXT NOT NULL,
    content_type TEXT NULL,
    size_bytes BIGINT NOT NULL DEFAULT 0,
    uploaded_by_user_id UUID REFERENCES app_users(user_id),
    uploaded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_purged BOOLEAN NOT NULL DEFAULT FALSE,
    purged_at TIMESTAMPTZ NULL,
    purged_by_user_id UUID REFERENCES app_users(user_id),
    purge_reason TEXT NULL
);

CREATE INDEX IF NOT EXISTS ix_project_engineer_allocations_user
ON project_engineer_allocations(user_id, is_active);

CREATE INDEX IF NOT EXISTS ix_project_document_files_project
ON project_document_files(project_allocation_project_id, document_type, is_purged);

INSERT INTO app_permissions (
    permission_code,
    permission_name,
    module_code,
    permission_description
)
VALUES
    ('VIEW_PROJECT_ALLOCATION_INFO', 'View Project Allocation and Info', 'projects', 'View assigned project allocations, SOW/GSD document links, allocated hours, used hours, and remaining hours.'),
    ('MANAGE_PROJECT_ALLOCATION_INFO', 'Manage Project Allocation and Info', 'projects', 'Create project allocation records, upload SOW/GSD files, and allocate engineer hours.'),
    ('PURGE_PROJECT_DOCUMENTS', 'Purge Project Documents', 'projects', 'Purge SOW/GSD files older than the configured retention period.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

-- Engineers can view their own allocation info.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code = 'VIEW_PROJECT_ALLOCATION_INFO'
WHERE r.role_code IN ('ENGINEER', 'MANAGER', 'PROJECT_MANAGER', 'PROJECT_TEAM_COORDINATOR', 'ADMINISTRATOR')
ON CONFLICT DO NOTHING;

-- PM, Project/Team Coordinator, and Admin can manage allocations/uploads.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code = 'MANAGE_PROJECT_ALLOCATION_INFO'
WHERE r.role_code IN ('PROJECT_MANAGER', 'PROJECT_TEAM_COORDINATOR', 'ADMINISTRATOR')
ON CONFLICT DO NOTHING;

-- Purge is intentionally limited.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code = 'PURGE_PROJECT_DOCUMENTS'
WHERE r.role_code IN ('PROJECT_TEAM_COORDINATOR', 'ADMINISTRATOR')
ON CONFLICT DO NOTHING;

INSERT INTO app_feature_catalog (
    feature_code,
    feature_name,
    module_code,
    route_anchor,
    required_permission_code,
    feature_description,
    display_order,
    is_active
)
VALUES (
    'PROJECT_ALLOCATION_INFO',
    'Project Allocation and Info',
    'projects',
    '#project-allocation-info',
    'VIEW_PROJECT_ALLOCATION_INFO',
    'View project allocations, engineer hours, SOW/GSD downloads, and project information.',
    75,
    TRUE
)
ON CONFLICT (feature_code) DO UPDATE
SET feature_name = EXCLUDED.feature_name,
    module_code = EXCLUDED.module_code,
    route_anchor = EXCLUDED.route_anchor,
    required_permission_code = EXCLUDED.required_permission_code,
    feature_description = EXCLUDED.feature_description,
    display_order = EXCLUDED.display_order,
    is_active = TRUE,
    updated_at = NOW();

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '019i_project_allocation_info',
    'Project Allocation and Info page with SOW/GSD document upload, engineer allocation, and document purge foundation',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
