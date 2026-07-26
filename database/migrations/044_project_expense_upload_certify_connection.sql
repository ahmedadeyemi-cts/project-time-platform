-- ProjectPulse Modules 005 and 038
-- Project Expense Upload plus Certify connection foundation.
-- Supports transaction-level GL exports, category-summary exports, CSV imports,
-- controlled Certify imports, versioned replacement, invoice treatment, audit,
-- and global-mail notification delivery.

BEGIN;

CREATE TABLE IF NOT EXISTS project_expense_uploads (
    project_expense_upload_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE RESTRICT,
    customer_name TEXT NOT NULL,
    project_code TEXT NOT NULL,
    project_name TEXT NOT NULL,
    expense_owner_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    uploaded_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    source_mode TEXT NOT NULL CHECK (source_mode IN ('excel_csv', 'certify')),
    source_format TEXT NOT NULL CHECK (source_format IN ('gl_dimension', 'category_summary', 'csv_gl_dimension', 'csv_category_summary', 'certify_api')),
    source_report_id TEXT NULL,
    original_file_name TEXT NULL,
    content_type TEXT NULL,
    source_file_bytes BYTEA NULL,
    source_sha256 TEXT NOT NULL,
    source_metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    period_start DATE NULL,
    period_end DATE NULL,
    currency TEXT NOT NULL DEFAULT 'USD',
    line_count INTEGER NOT NULL DEFAULT 0 CHECK (line_count >= 0),
    total_amount NUMERIC(14,2) NOT NULL DEFAULT 0,
    reimbursable_amount NUMERIC(14,2) NOT NULL DEFAULT 0,
    contract_type_snapshot TEXT NOT NULL DEFAULT '',
    billing_treatment TEXT NOT NULL CHECK (billing_treatment IN ('pass_through_invoice', 'included_fixed_price', 'internal_nonbillable')),
    version_number INTEGER NOT NULL CHECK (version_number > 0),
    is_current BOOLEAN NOT NULL DEFAULT TRUE,
    uploaded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    deleted_at TIMESTAMPTZ NULL,
    deleted_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    deletion_reason TEXT NULL,
    notification_status TEXT NOT NULL DEFAULT 'pending' CHECK (notification_status IN ('pending', 'sent', 'queued', 'configuration_pending', 'failed')),
    notification_detail TEXT NOT NULL DEFAULT '',
    UNIQUE (project_id, expense_owner_user_id, period_start, period_end, version_number)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_project_expense_current_period
ON project_expense_uploads (
    project_id,
    expense_owner_user_id,
    COALESCE(period_start, DATE '1900-01-01'),
    COALESCE(period_end, DATE '9999-12-31')
)
WHERE is_current = TRUE AND deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS ix_project_expense_uploads_project
ON project_expense_uploads(project_id, uploaded_at DESC);

CREATE INDEX IF NOT EXISTS ix_project_expense_uploads_owner
ON project_expense_uploads(expense_owner_user_id, uploaded_at DESC);

CREATE TABLE IF NOT EXISTS project_expense_lines (
    project_expense_line_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_expense_upload_id UUID NOT NULL REFERENCES project_expense_uploads(project_expense_upload_id) ON DELETE CASCADE,
    line_number INTEGER NOT NULL CHECK (line_number > 0),
    employee_name TEXT NOT NULL DEFAULT '',
    employee_email TEXT NOT NULL DEFAULT '',
    department_name TEXT NOT NULL DEFAULT '',
    department_code TEXT NOT NULL DEFAULT '',
    expense_date DATE NULL,
    expense_category TEXT NOT NULL,
    gl_code TEXT NOT NULL DEFAULT '',
    amount NUMERIC(14,2) NOT NULL,
    reimbursable BOOLEAN NOT NULL DEFAULT TRUE,
    reimbursable_amount NUMERIC(14,2) NOT NULL DEFAULT 0,
    currency TEXT NOT NULL DEFAULT 'USD',
    reason TEXT NOT NULL DEFAULT '',
    is_summary_line BOOLEAN NOT NULL DEFAULT FALSE,
    source_row JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(project_expense_upload_id, line_number)
);

CREATE INDEX IF NOT EXISTS ix_project_expense_lines_upload
ON project_expense_lines(project_expense_upload_id, line_number);

CREATE INDEX IF NOT EXISTS ix_project_expense_lines_category
ON project_expense_lines(expense_category, expense_date);

CREATE TABLE IF NOT EXISTS project_expense_events (
    project_expense_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_expense_upload_id UUID NULL REFERENCES project_expense_uploads(project_expense_upload_id) ON DELETE RESTRICT,
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE RESTRICT,
    event_code TEXT NOT NULL CHECK (event_code IN ('UPLOAD_CREATED', 'CERTIFY_IMPORTED', 'UPLOAD_SUPERSEDED', 'UPLOAD_DELETED', 'PRIOR_VERSION_RESTORED', 'NOTIFICATION_QUEUED', 'NOTIFICATION_SENT', 'NOTIFICATION_FAILED')),
    actor_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    target_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    reason TEXT NOT NULL DEFAULT '',
    event_metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_project_expense_events_project
ON project_expense_events(project_id, created_at DESC);

CREATE OR REPLACE FUNCTION projectpulse044_block_expense_event_mutation()
RETURNS trigger LANGUAGE plpgsql AS $projectpulse044_expense_event_immutable$
BEGIN
    RAISE EXCEPTION 'Project expense audit events are immutable.';
END;
$projectpulse044_expense_event_immutable$;

DROP TRIGGER IF EXISTS trg_projectpulse044_expense_events_immutable
ON project_expense_events;
CREATE TRIGGER trg_projectpulse044_expense_events_immutable
BEFORE UPDATE OR DELETE ON project_expense_events
FOR EACH ROW EXECUTE FUNCTION projectpulse044_block_expense_event_mutation();

CREATE TABLE IF NOT EXISTS project_expense_mail_outbox (
    project_expense_mail_outbox_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_expense_upload_id UUID NOT NULL REFERENCES project_expense_uploads(project_expense_upload_id) ON DELETE CASCADE,
    provider_source TEXT NOT NULL DEFAULT 'global_mail_configuration',
    to_addresses TEXT[] NOT NULL,
    cc_addresses TEXT[] NOT NULL DEFAULT ARRAY[]::TEXT[],
    subject TEXT NOT NULL,
    text_body TEXT NOT NULL,
    html_body TEXT NOT NULL,
    delivery_status TEXT NOT NULL DEFAULT 'queued' CHECK (delivery_status IN ('queued', 'sending', 'sent', 'configuration_pending', 'failed')),
    delivery_attempts INTEGER NOT NULL DEFAULT 0,
    provider_message_id TEXT NULL,
    last_error TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    sent_at TIMESTAMPTZ NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_project_expense_mail_outbox_status
ON project_expense_mail_outbox(delivery_status, created_at);

CREATE TABLE IF NOT EXISTS certify_connection_profiles (
    certify_connection_profile_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    profile_name TEXT NOT NULL UNIQUE DEFAULT 'default',
    environment_name TEXT NOT NULL DEFAULT 'test' CHECK (environment_name IN ('test', 'production')),
    base_url TEXT NOT NULL DEFAULT 'https://api.certify.com/v1/',
    authentication_mode TEXT NOT NULL DEFAULT 'api_key_secret' CHECK (authentication_mode IN ('api_key_secret')),
    api_key_environment_name TEXT NOT NULL DEFAULT 'PROJECTPULSE_CERTIFY_API_KEY',
    api_secret_environment_name TEXT NOT NULL DEFAULT 'PROJECTPULSE_CERTIFY_API_SECRET',
    company_id TEXT NOT NULL DEFAULT '',
    connection_status TEXT NOT NULL DEFAULT 'not_configured' CHECK (connection_status IN ('not_configured', 'configured', 'connected', 'failed')),
    automatic_sync_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    sync_cadence TEXT NOT NULL DEFAULT 'manual' CHECK (sync_cadence IN ('manual', 'hourly', 'nightly')),
    last_tested_at TIMESTAMPTZ NULL,
    last_test_result TEXT NOT NULL DEFAULT '',
    last_successful_sync_at TIMESTAMPTZ NULL,
    configured_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO certify_connection_profiles (profile_name)
VALUES ('default')
ON CONFLICT (profile_name) DO NOTHING;

CREATE TABLE IF NOT EXISTS certify_expense_import_runs (
    certify_expense_import_run_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    certify_connection_profile_id UUID NOT NULL REFERENCES certify_connection_profiles(certify_connection_profile_id) ON DELETE RESTRICT,
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE RESTRICT,
    expense_owner_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    initiated_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    import_status TEXT NOT NULL CHECK (import_status IN ('started', 'completed', 'failed', 'no_records')),
    certify_report_id TEXT NULL,
    request_metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    response_metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    imported_upload_id UUID NULL REFERENCES project_expense_uploads(project_expense_upload_id) ON DELETE SET NULL,
    error_detail TEXT NOT NULL DEFAULT '',
    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ NULL
);

CREATE INDEX IF NOT EXISTS ix_certify_import_runs_project
ON certify_expense_import_runs(project_id, started_at DESC);

CREATE OR REPLACE VIEW project_expense_current_summary AS
SELECT
    upload.project_expense_upload_id,
    upload.project_id,
    upload.customer_name,
    upload.project_code,
    upload.project_name,
    upload.expense_owner_user_id,
    owner_user.display_name AS expense_owner_name,
    owner_user.email AS expense_owner_email,
    upload.uploaded_by_user_id,
    uploader.display_name AS uploaded_by_name,
    upload.source_mode,
    upload.source_format,
    upload.original_file_name,
    upload.period_start,
    upload.period_end,
    upload.currency,
    upload.line_count,
    upload.total_amount,
    upload.reimbursable_amount,
    upload.contract_type_snapshot,
    upload.billing_treatment,
    upload.version_number,
    upload.uploaded_at,
    upload.notification_status,
    upload.notification_detail
FROM project_expense_uploads upload
JOIN app_users owner_user ON owner_user.user_id = upload.expense_owner_user_id
JOIN app_users uploader ON uploader.user_id = upload.uploaded_by_user_id
WHERE upload.is_current = TRUE
  AND upload.deleted_at IS NULL;

INSERT INTO app_permissions (
    permission_code,
    permission_name,
    module_code,
    permission_description
)
VALUES
    ('VIEW_PROJECT_EXPENSE_UPLOAD', 'View Project Expense Upload', '005', 'View project expense uploads for projects within the current role scope.'),
    ('UPLOAD_PROJECT_EXPENSE_SELF', 'Upload Own Project Expenses', '005', 'Upload or import project expenses for the current user on an assigned project.'),
    ('UPLOAD_PROJECT_EXPENSE_ON_BEHALF', 'Upload Project Expenses On Behalf', '005', 'Upload or import project expenses for an engineer assigned to a project.'),
    ('DELETE_PROJECT_EXPENSE_UPLOAD', 'Delete and Replace Project Expense Upload', '005', 'Soft-delete a project expense upload and restore the prior version when available.'),
    ('IMPORT_PROJECT_EXPENSE_CERTIFY', 'Import Project Expenses from Certify', '005', 'Import approved expense data using the governed Module 038 Certify connection.'),
    ('VIEW_PROJECT_EXPENSE_INVOICE_CONTEXT', 'View Project Expenses in Billing Context', '042', 'View current project expense totals and invoice treatment in Module 042 and Module 055C.'),
    ('MANAGE_CERTIFY_CONNECTION', 'Manage Certify Connection', '038', 'Configure and test the Certify API connection without exposing secret values.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission
  ON permission.permission_code IN (
      'VIEW_PROJECT_EXPENSE_UPLOAD',
      'UPLOAD_PROJECT_EXPENSE_SELF',
      'DELETE_PROJECT_EXPENSE_UPLOAD'
  )
WHERE upper(role.role_code) IN (
    'ENGINEER', 'ENGINEERING',
    'ENGINEERING_LEAD', 'ENGINEERING_TEAM_LEAD',
    'PROJECT_MANAGER', 'PROJECT_MANAGEMENT',
    'PROJECT_MANAGEMENT_LEAD', 'PROJECT_MANAGEMENT_TEAM_LEAD', 'PM_TEAM_LEAD',
    'SUPER_ADMINISTRATOR', 'ADMINISTRATOR'
)
ON CONFLICT DO NOTHING;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission
  ON permission.permission_code IN (
      'UPLOAD_PROJECT_EXPENSE_ON_BEHALF',
      'IMPORT_PROJECT_EXPENSE_CERTIFY',
      'VIEW_PROJECT_EXPENSE_INVOICE_CONTEXT'
  )
WHERE upper(role.role_code) IN (
    'PROJECT_MANAGER', 'PROJECT_MANAGEMENT',
    'PROJECT_MANAGEMENT_LEAD', 'PROJECT_MANAGEMENT_TEAM_LEAD', 'PM_TEAM_LEAD',
    'SUPER_ADMINISTRATOR', 'ADMINISTRATOR'
)
ON CONFLICT DO NOTHING;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code = 'MANAGE_CERTIFY_CONNECTION'
WHERE upper(role.role_code) IN ('SUPER_ADMINISTRATOR', 'ADMINISTRATOR', 'ACCOUNTING')
ON CONFLICT DO NOTHING;

UPDATE app_feature_catalog
SET feature_name = 'Project Expense Upload',
    module_code = '005',
    required_permission_code = 'VIEW_PROJECT_EXPENSE_UPLOAD',
    feature_description = 'Select a customer and project, upload Certify expense exports, manage versions, and track invoice treatment.',
    updated_at = NOW()
WHERE feature_code = 'PROJECT_ALLOCATION_INFO';

INSERT INTO app_feature_catalog (
    feature_code, feature_name, module_code, route_anchor,
    required_permission_code, feature_description, display_order, is_active
)
VALUES (
    'PROJECT_EXPENSE_UPLOAD',
    'Project Expense Upload',
    '005',
    '#project-allocation-info',
    'VIEW_PROJECT_EXPENSE_UPLOAD',
    'Upload Excel or CSV expense exports, import through Certify, and associate expenses to a customer, project, and expense owner.',
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

UPDATE scoped_role_policy_modules
SET module_name = 'Project Expense Upload',
    current_state = 'Installed',
    permission_notes = 'Engineers and delivery leads upload for themselves; Project Management and PM Leads may upload on behalf of assigned engineers. T&M expenses are invoice pass-through; Fixed Price expenses are tracked as included cost.'
WHERE module_code = '005';

UPDATE scoped_role_policy_modules
SET module_name = 'Certify Connection & Sync Center',
    current_state = 'Installed',
    permission_notes = 'Connection metadata and secret environment references are managed in Module 038. Automatic sync remains disabled until the connection is tested and explicitly enabled.'
WHERE module_code = '038';

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '044_project_expense_upload_certify_connection',
    'Module 005 Project Expense Upload and Module 038 Certify connection with normalized Excel/CSV imports, versioning, billing treatment, global-mail notification outbox, and billing/work-register visibility',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
