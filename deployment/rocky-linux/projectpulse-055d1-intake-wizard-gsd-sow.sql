-- 055D.1 - Initial Intake Wizard with GSD/SOW Upload
-- Stores intake packages before they become Work Register records.

BEGIN;

CREATE TABLE IF NOT EXISTS work_register_intake_packages (
    work_register_intake_package_id uuid PRIMARY KEY,
    intake_status varchar(60) NOT NULL DEFAULT 'uploaded',
    requested_work_type varchar(80) NOT NULL DEFAULT 'Project',
    source_mode varchar(60) NOT NULL DEFAULT 'gsd_sow_upload',
    customer_hint text NOT NULL DEFAULT '',
    project_name_hint text NOT NULL DEFAULT '',
    notes text NOT NULL DEFAULT '',
    extraction_status varchar(60) NOT NULL DEFAULT 'pending_parser',
    extracted_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_by_user_id uuid NULL,
    created_at timestamp with time zone NOT NULL DEFAULT NOW(),
    updated_at timestamp with time zone NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS work_register_intake_documents (
    work_register_intake_document_id uuid PRIMARY KEY,
    work_register_intake_package_id uuid NOT NULL,
    document_type varchar(80) NOT NULL,
    original_file_name text NOT NULL DEFAULT '',
    stored_file_path text NOT NULL DEFAULT '',
    content_type text NOT NULL DEFAULT '',
    file_size_bytes bigint NOT NULL DEFAULT 0,
    uploaded_by_user_id uuid NULL,
    created_at timestamp with time zone NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS work_register_intake_history (
    work_register_intake_history_id uuid PRIMARY KEY,
    work_register_intake_package_id uuid NOT NULL,
    action varchar(120) NOT NULL DEFAULT '',
    summary text NOT NULL DEFAULT '',
    changed_by_user_id uuid NULL,
    payload_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamp with time zone NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_work_register_intake_packages_status
    ON work_register_intake_packages(intake_status, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_work_register_intake_packages_created
    ON work_register_intake_packages(created_at DESC);

CREATE INDEX IF NOT EXISTS idx_work_register_intake_documents_package
    ON work_register_intake_documents(work_register_intake_package_id);

CREATE INDEX IF NOT EXISTS idx_work_register_intake_history_package
    ON work_register_intake_history(work_register_intake_package_id, created_at DESC);

COMMIT;
