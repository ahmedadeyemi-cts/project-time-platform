-- 019M-P production-shaped intake source tracking.
-- Supports Salesforce source references, manual uploaded documents, and direct manual entry.

ALTER TABLE project_intake_requests
ADD COLUMN IF NOT EXISTS intake_source character varying(60) NOT NULL DEFAULT 'manual_entry',
ADD COLUMN IF NOT EXISTS source_system character varying(80) NULL,
ADD COLUMN IF NOT EXISTS external_reference_id character varying(160) NULL,
ADD COLUMN IF NOT EXISTS external_record_type character varying(80) NULL,
ADD COLUMN IF NOT EXISTS external_record_url text NULL,
ADD COLUMN IF NOT EXISTS source_received_at timestamp with time zone NULL,
ADD COLUMN IF NOT EXISTS source_document_required boolean NOT NULL DEFAULT false,
ADD COLUMN IF NOT EXISTS source_document_received boolean NOT NULL DEFAULT false,
ADD COLUMN IF NOT EXISTS intake_source_notes text NULL;

CREATE INDEX IF NOT EXISTS idx_project_intake_requests_source
ON project_intake_requests(intake_source, source_system, external_reference_id);

CREATE UNIQUE INDEX IF NOT EXISTS uq_project_intake_external_reference
ON project_intake_requests(source_system, external_reference_id)
WHERE source_system IS NOT NULL
  AND external_reference_id IS NOT NULL;

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

CREATE INDEX IF NOT EXISTS idx_project_intake_documents_request
ON project_intake_documents(project_intake_request_id, is_active);

UPDATE project_intake_requests
SET intake_source = 'salesforce',
    source_system = 'Salesforce',
    external_reference_id = opportunity_reference,
    external_record_type = 'Opportunity',
    source_received_at = COALESCE(source_received_at, created_at)
WHERE opportunity_reference IS NOT NULL
  AND opportunity_reference <> ''
  AND opportunity_reference ILIKE 'OPP-%';

UPDATE project_intake_requests
SET intake_source = 'manual_entry'
WHERE intake_source IS NULL
   OR intake_source = '';
