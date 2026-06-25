BEGIN;

CREATE TABLE IF NOT EXISTS azure_entra_import_settings (
    settings_id TEXT PRIMARY KEY DEFAULT 'default',
    environment_mode TEXT NOT NULL DEFAULT 'test',
    tenant_domain TEXT NOT NULL DEFAULT 'onenecklab.com',
    source_provider TEXT NOT NULL DEFAULT 'ENTRA_ID_TEST',
    import_source_type TEXT NOT NULL DEFAULT 'ALL_USERS',
    graph_group_id TEXT NULL,
    graph_filter TEXT NULL,
    default_role_code TEXT NOT NULL DEFAULT 'ENGINEER',
    disable_missing_from_source BOOLEAN NOT NULL DEFAULT TRUE,
    last_preview_at TIMESTAMPTZ NULL,
    last_import_at TIMESTAMPTZ NULL,
    last_reconcile_at TIMESTAMPTZ NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_azure_entra_import_source_type
        CHECK (import_source_type IN ('ALL_USERS', 'GROUP', 'FILTER')),
    CONSTRAINT ck_azure_entra_import_source_provider
        CHECK (source_provider IN ('ENTRA_ID', 'ENTRA_ID_TEST')),
    CONSTRAINT ck_azure_entra_import_environment_mode
        CHECK (environment_mode IN ('test', 'production'))
);

INSERT INTO azure_entra_import_settings (
    settings_id,
    environment_mode,
    tenant_domain,
    source_provider,
    import_source_type,
    default_role_code,
    disable_missing_from_source
)
VALUES (
    'default',
    'test',
    'onenecklab.com',
    'ENTRA_ID_TEST',
    'ALL_USERS',
    'ENGINEER',
    TRUE
)
ON CONFLICT (settings_id) DO NOTHING;

CREATE TABLE IF NOT EXISTS azure_entra_import_runs (
    import_run_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    run_type TEXT NOT NULL,
    environment_mode TEXT NOT NULL,
    tenant_domain TEXT NOT NULL,
    source_provider TEXT NOT NULL,
    import_source_type TEXT NOT NULL,
    graph_group_id TEXT NULL,
    graph_filter TEXT NULL,
    requested_by_user_id UUID NULL REFERENCES app_users(user_id),
    previewed_count INTEGER NOT NULL DEFAULT 0,
    selected_count INTEGER NOT NULL DEFAULT 0,
    imported_count INTEGER NOT NULL DEFAULT 0,
    updated_count INTEGER NOT NULL DEFAULT 0,
    deactivated_count INTEGER NOT NULL DEFAULT 0,
    skipped_count INTEGER NOT NULL DEFAULT 0,
    status TEXT NOT NULL DEFAULT 'completed',
    message TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS azure_entra_import_run_users (
    import_run_user_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    import_run_id UUID NOT NULL REFERENCES azure_entra_import_runs(import_run_id) ON DELETE CASCADE,
    entra_object_id TEXT NULL,
    email TEXT NULL,
    display_name TEXT NULL,
    account_enabled BOOLEAN NULL,
    action_taken TEXT NOT NULL,
    message TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '019m_b_azure_graph_selective_import',
    'Azure Graph selective import settings, audit run tables, and inactive reconciliation support',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
