-- Disposable query-contract fixture, not a production migration or role grant.
CREATE TABLE project_intake_documents (
 project_intake_document_id uuid PRIMARY KEY, is_active boolean NOT NULL,
 pulse_ai_processing_status text, pulse_ai_processing_error_code text, pulse_ai_active_version_id uuid
);
CREATE TABLE pulse_ai_document_versions (
 pulse_ai_document_version_id uuid PRIMARY KEY, project_intake_document_id uuid NOT NULL,
 source_sha256 text, authority_status text, extraction_method text, malware_scanner text, processed_by_job_id uuid
);
CREATE TABLE pulse_ai_document_processing_jobs (
 pulse_ai_document_processing_job_id uuid PRIMARY KEY, project_intake_document_id uuid NOT NULL,
 source_sha256 text, job_status text, malware_scanner text
);
CREATE TABLE pulse_ai_document_sections (
 pulse_ai_document_version_id uuid, project_intake_document_id uuid, section_index integer,
 section_text text, text_sha256 text
);
CREATE TABLE pulse_ai_document_processing_events (
 pulse_ai_document_processing_job_id uuid, project_intake_document_id uuid,
 event_code text, event_status text, evidence_json jsonb
);
