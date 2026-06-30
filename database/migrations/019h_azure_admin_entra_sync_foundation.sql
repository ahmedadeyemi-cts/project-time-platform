BEGIN;

ALTER TABLE app_users
ADD COLUMN IF NOT EXISTS entra_object_id TEXT NULL;

ALTER TABLE app_users
ADD COLUMN IF NOT EXISTS source_provider TEXT NOT NULL DEFAULT 'LOCAL_APP';

ALTER TABLE app_users
ADD COLUMN IF NOT EXISTS job_title TEXT NULL;

ALTER TABLE app_users
ADD COLUMN IF NOT EXISTS department_name TEXT NULL;

ALTER TABLE app_users
ADD COLUMN IF NOT EXISTS office_location TEXT NULL;

ALTER TABLE app_users
ADD COLUMN IF NOT EXISTS manager_email TEXT NULL;

ALTER TABLE app_users
ADD COLUMN IF NOT EXISTS last_directory_sync_at TIMESTAMPTZ NULL;

ALTER TABLE app_users
ADD COLUMN IF NOT EXISTS login_enabled BOOLEAN NOT NULL DEFAULT TRUE;

CREATE UNIQUE INDEX IF NOT EXISTS ux_app_users_entra_object_id
ON app_users(entra_object_id)
WHERE entra_object_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS azure_entra_settings (
    azure_entra_settings_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id TEXT NULL,
    client_id TEXT NULL,
    authority_url TEXT NULL,
    redirect_uri TEXT NULL,
    graph_scope TEXT NOT NULL DEFAULT 'User.Read.All Directory.Read.All',
    sync_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    default_role_code TEXT NOT NULL DEFAULT 'ENGINEER',
    sync_frequency_hours INTEGER NOT NULL DEFAULT 24,
    last_sync_at TIMESTAMPTZ NULL,
    last_sync_status TEXT NULL,
    last_sync_message TEXT NULL,
    updated_by_email TEXT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO azure_entra_settings (
    tenant_id,
    client_id,
    authority_url,
    redirect_uri,
    sync_enabled,
    default_role_code,
    sync_frequency_hours,
    last_sync_status,
    last_sync_message
)
SELECT
    NULL,
    NULL,
    NULL,
    NULL,
    FALSE,
    'ENGINEER',
    24,
    'not_configured',
    'Azure/Entra sync foundation created. Configure tenant and client settings before enabling real Microsoft Graph sync.'
WHERE NOT EXISTS (SELECT 1 FROM azure_entra_settings);

CREATE TABLE IF NOT EXISTS azure_entra_sync_runs (
    azure_entra_sync_run_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    sync_started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    sync_completed_at TIMESTAMPTZ NULL,
    status TEXT NOT NULL DEFAULT 'started',
    triggered_by_email TEXT NULL,
    users_seen INTEGER NOT NULL DEFAULT 0,
    users_imported INTEGER NOT NULL DEFAULT 0,
    users_updated INTEGER NOT NULL DEFAULT 0,
    users_skipped INTEGER NOT NULL DEFAULT 0,
    message TEXT NULL
);

CREATE TABLE IF NOT EXISTS azure_entra_sync_errors (
    azure_entra_sync_error_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    azure_entra_sync_run_id UUID REFERENCES azure_entra_sync_runs(azure_entra_sync_run_id) ON DELETE CASCADE,
    user_email TEXT NULL,
    error_message TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO app_permissions (
    permission_code,
    permission_name,
    module_code,
    permission_description
)
VALUES
    ('VIEW_AZURE_ADMIN', 'View Azure Admin', 'admin', 'View Azure/Entra configuration, sync status, and imported users.'),
    ('MANAGE_AZURE_SYNC', 'Manage Azure Sync', 'admin', 'Configure Azure/Entra sync and run user import/sync operations.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code IN ('VIEW_AZURE_ADMIN', 'MANAGE_AZURE_SYNC')
WHERE r.role_code = 'ADMINISTRATOR'
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
    'AZURE_ADMIN',
    'Azure Admin',
    'admin',
    '#azure-admin',
    'VIEW_AZURE_ADMIN',
    'Azure/Entra configuration, user sync, imported users, and default role assignment.',
    160,
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
    '019h_azure_admin_entra_sync_foundation',
    'Azure Admin and Entra user sync foundation',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
