-- 055C.10 - Local document upload metadata for Work Register documents

BEGIN;

ALTER TABLE work_register_documents
    ADD COLUMN IF NOT EXISTS upload_source varchar(40) NOT NULL DEFAULT 'link';

ALTER TABLE work_register_documents
    ADD COLUMN IF NOT EXISTS original_file_name text NOT NULL DEFAULT '';

ALTER TABLE work_register_documents
    ADD COLUMN IF NOT EXISTS stored_file_path text NOT NULL DEFAULT '';

ALTER TABLE work_register_documents
    ADD COLUMN IF NOT EXISTS content_type text NOT NULL DEFAULT '';

ALTER TABLE work_register_documents
    ADD COLUMN IF NOT EXISTS file_size_bytes bigint NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS idx_work_register_documents_upload_source
    ON work_register_documents(project_id, upload_source);

COMMIT;
