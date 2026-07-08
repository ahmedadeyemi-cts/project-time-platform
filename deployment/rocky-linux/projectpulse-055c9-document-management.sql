-- 055C.9 - Work Register document management
-- Registers/links project documents from Work Register without replacing existing project_documents.

BEGIN;

CREATE TABLE IF NOT EXISTS work_register_documents (
    work_register_document_id uuid PRIMARY KEY,
    project_id uuid NOT NULL,
    document_name text NOT NULL DEFAULT '',
    document_type varchar(80) NOT NULL DEFAULT 'Other',
    document_reference text NOT NULL DEFAULT '',
    version_label varchar(80) NOT NULL DEFAULT '',
    status varchar(40) NOT NULL DEFAULT 'active',
    visibility varchar(40) NOT NULL DEFAULT 'project_team',
    related_change_order_id uuid NULL,
    effective_date date NULL,
    notes text NOT NULL DEFAULT '',
    created_by_user_id uuid NULL,
    created_at timestamp with time zone NOT NULL DEFAULT NOW(),
    archived_by_user_id uuid NULL,
    archived_at timestamp with time zone NULL,
    archive_reason text NOT NULL DEFAULT ''
);

CREATE INDEX IF NOT EXISTS idx_work_register_documents_project
    ON work_register_documents(project_id);

CREATE INDEX IF NOT EXISTS idx_work_register_documents_status
    ON work_register_documents(project_id, status);

CREATE INDEX IF NOT EXISTS idx_work_register_documents_type
    ON work_register_documents(project_id, document_type);

CREATE INDEX IF NOT EXISTS idx_work_register_documents_created
    ON work_register_documents(created_at DESC);

COMMIT;
