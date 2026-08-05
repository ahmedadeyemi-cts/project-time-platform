\set ON_ERROR_STOP on

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE schema_migrations (
    migration_id TEXT PRIMARY KEY,
    description TEXT NOT NULL DEFAULT '',
    applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE app_users (
    user_id UUID PRIMARY KEY,
    email TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE clients (
    client_id UUID PRIMARY KEY,
    client_name TEXT NOT NULL
);

CREATE TABLE projects (
    project_id UUID PRIMARY KEY,
    client_id UUID NULL REFERENCES clients(client_id),
    project_code TEXT NOT NULL,
    project_name TEXT NOT NULL,
    project_manager_user_id UUID NULL REFERENCES app_users(user_id)
);

CREATE TABLE app_roles (
    app_role_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    role_code TEXT NOT NULL UNIQUE,
    role_name TEXT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE app_permissions (
    app_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    permission_code TEXT NOT NULL UNIQUE,
    permission_name TEXT NOT NULL,
    module_code TEXT NOT NULL,
    permission_description TEXT NOT NULL DEFAULT ''
);

CREATE TABLE app_role_permissions (
    app_role_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE CASCADE,
    app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE CASCADE,
    UNIQUE(app_role_id, app_permission_id)
);

CREATE TABLE app_feature_catalog (
    app_feature_catalog_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    feature_code TEXT NOT NULL UNIQUE,
    feature_name TEXT NOT NULL,
    module_code TEXT NOT NULL,
    route_anchor TEXT,
    required_permission_code TEXT,
    feature_description TEXT,
    display_order INTEGER NOT NULL DEFAULT 0,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Production installations may already have the original runtime-owned Module
-- 064 tables. Migration 071 must upgrade this exact legacy shape in place; a
-- greenfield-only test would not exercise its ALTER/validation path.
CREATE TABLE ai_provider_secrets (
    provider_code TEXT PRIMARY KEY,
    ciphertext BYTEA NOT NULL,
    nonce BYTEA NOT NULL,
    tag BYTEA NOT NULL,
    version TEXT NOT NULL,
    rotated_at TIMESTAMPTZ NOT NULL,
    rotated_by UUID NOT NULL
);

CREATE TABLE ai_provider_secret_audit (
    audit_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    provider_code TEXT NOT NULL,
    action TEXT NOT NULL,
    version TEXT NOT NULL,
    actor_user_id UUID NOT NULL,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE ai_provider_settings (
    provider_code TEXT PRIMARY KEY,
    model TEXT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_by UUID NOT NULL
);

CREATE TABLE ai_provider_settings_audit (
    audit_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    provider_code TEXT NOT NULL,
    action TEXT NOT NULL,
    model TEXT NOT NULL,
    actor_user_id UUID NOT NULL,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE project_intake_requests (
    project_intake_request_id UUID PRIMARY KEY DEFAULT gen_random_uuid()
);

CREATE TABLE project_intake_documents (
    project_intake_document_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_intake_request_id UUID NOT NULL REFERENCES project_intake_requests(project_intake_request_id) ON DELETE CASCADE,
    project_id UUID NULL REFERENCES projects(project_id),
    document_type VARCHAR(80) NOT NULL DEFAULT 'other',
    document_category VARCHAR(80) NOT NULL DEFAULT 'other',
    original_file_name TEXT NOT NULL,
    stored_file_name TEXT NOT NULL,
    storage_path TEXT NOT NULL,
    content_type TEXT NULL,
    size_bytes BIGINT NOT NULL DEFAULT 0,
    engineering_visible BOOLEAN NOT NULL DEFAULT TRUE,
    ai_timesheet_context_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    extraction_status VARCHAR(60) NOT NULL DEFAULT 'not_started',
    ai_context_summary TEXT NULL,
    ai_context_last_processed_at TIMESTAMPTZ NULL,
    upload_source VARCHAR(60) NOT NULL DEFAULT 'manual_upload',
    uploaded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

INSERT INTO app_users(user_id, email, display_name)
VALUES ('10000000-0000-0000-0000-000000000001', 'celar-ci@example.invalid', 'Celar CI Service Principal');

INSERT INTO ai_provider_secrets(
    provider_code, ciphertext, nonce, tag, version, rotated_at, rotated_by)
VALUES (
    'claude',
    decode(repeat('ab', 24), 'hex'),
    decode(repeat('cd', 12), 'hex'),
    decode(repeat('ef', 16), 'hex'),
    'legacy-ci',
    NOW(),
    '10000000-0000-0000-0000-000000000001'
);

INSERT INTO ai_provider_settings(provider_code, model, updated_by)
VALUES ('claude', 'legacy-ci-model', '10000000-0000-0000-0000-000000000001');

INSERT INTO clients(client_id, client_name)
VALUES ('20000000-0000-0000-0000-000000000001', 'Synthetic CI Client');

INSERT INTO projects(project_id, client_id, project_code, project_name, project_manager_user_id)
VALUES (
    '30000000-0000-0000-0000-000000000001',
    '20000000-0000-0000-0000-000000000001',
    'P-CELAR-CI',
    'Celar Production Hardening CI',
    '10000000-0000-0000-0000-000000000001'
);

INSERT INTO project_intake_requests(project_intake_request_id)
VALUES ('40000000-0000-0000-0000-000000000001');

INSERT INTO project_intake_documents(
    project_intake_document_id,
    project_intake_request_id,
    project_id,
    document_type,
    document_category,
    original_file_name,
    stored_file_name,
    storage_path,
    engineering_visible,
    ai_timesheet_context_enabled
)
VALUES (
    '50000000-0000-0000-0000-000000000001',
    '40000000-0000-0000-0000-000000000001',
    '30000000-0000-0000-0000-000000000001',
    'sow',
    'sow',
    'synthetic-sow.pdf',
    'synthetic-sow.pdf',
    '/mnt/projectpulse-ci/synthetic-sow.pdf',
    TRUE,
    TRUE
);

INSERT INTO app_roles(role_code, role_name)
VALUES
    ('SUPER_ADMINISTRATOR', 'Super Administrator'),
    ('ADMINISTRATOR', 'Administrator'),
    ('PROJECT_TEAM_COORDINATOR', 'Project Team Coordinator'),
    ('PROJECT_MANAGEMENT', 'Project Management'),
    ('ENGINEER', 'Engineer'),
    ('FINANCE', 'Finance');
