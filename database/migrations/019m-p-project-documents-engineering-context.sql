-- 019M-P production-shaped project intake documents.
-- SOW/GSD/supporting documents remain available to intake, project workspace,
-- engineering pages, and future AI-assisted timesheet description workflows.

CREATE TABLE IF NOT EXISTS project_intake_documents (
    project_intake_document_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    project_intake_request_id uuid NOT NULL REFERENCES project_intake_requests(project_intake_request_id) ON DELETE CASCADE,
    document_type character varying(80) NOT NULL DEFAULT 'intake_document',
    original_file_name text NOT NULL,
    stored_file_name text NOT NULL,
    storage_path text NOT NULL,
    content_type text NULL,
    size_bytes bigint NOT NULL DEFAULT 0,
    uploaded_by_user_id uuid NULL REFERENCES app_users(user_id),
    upload_source character varying(60) NOT NULL DEFAULT 'manual_upload',
    extraction_status character varying(60) NOT NULL DEFAULT 'not_started',
    extraction_notes text NULL,
    uploaded_at timestamp with time zone NOT NULL DEFAULT now(),
    is_active boolean NOT NULL DEFAULT true
);

ALTER TABLE project_intake_documents
ADD COLUMN IF NOT EXISTS project_id uuid NULL REFERENCES projects(project_id),
ADD COLUMN IF NOT EXISTS document_category character varying(80) NOT NULL DEFAULT 'other',
ADD COLUMN IF NOT EXISTS document_status character varying(60) NOT NULL DEFAULT 'active',
ADD COLUMN IF NOT EXISTS engineering_visible boolean NOT NULL DEFAULT true,
ADD COLUMN IF NOT EXISTS ai_timesheet_context_enabled boolean NOT NULL DEFAULT false,
ADD COLUMN IF NOT EXISTS ai_context_summary text NULL,
ADD COLUMN IF NOT EXISTS ai_context_last_processed_at timestamp with time zone NULL,
ADD COLUMN IF NOT EXISTS source_system character varying(80) NULL,
ADD COLUMN IF NOT EXISTS external_reference_id character varying(160) NULL;

CREATE INDEX IF NOT EXISTS idx_project_intake_documents_request
ON project_intake_documents(project_intake_request_id, is_active);

CREATE INDEX IF NOT EXISTS idx_project_intake_documents_project
ON project_intake_documents(project_id, engineering_visible, is_active);

CREATE INDEX IF NOT EXISTS idx_project_intake_documents_ai_context
ON project_intake_documents(ai_timesheet_context_enabled, engineering_visible, is_active);

UPDATE project_intake_documents
SET document_category =
    CASE
        WHEN lower(document_type) IN ('sow', 'statement_of_work') THEN 'sow'
        WHEN lower(document_type) IN ('gsd', 'global_solution_design') THEN 'gsd'
        WHEN lower(document_type) IN ('quote', 'proposal') THEN 'quote'
        ELSE COALESCE(NULLIF(document_category, ''), 'other')
    END;
