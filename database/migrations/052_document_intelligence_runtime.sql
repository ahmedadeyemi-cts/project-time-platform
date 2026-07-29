-- Pulse AI Module 011 — Phase 011C
-- Durable private document processing, citation-preserving storage, private
-- embeddings, permission-scoped hybrid retrieval metadata, and immutable audit.
--
-- This migration creates only ProjectPulse-owned PostgreSQL structures. It does
-- not configure an OCR endpoint, malware scanner, embedding service, model,
-- Module 064 route, Azure resource, or external provider.

BEGIN;

ALTER TABLE project_intake_documents
    ADD COLUMN IF NOT EXISTS pulse_ai_processing_status VARCHAR(40) NOT NULL DEFAULT 'not_requested',
    ADD COLUMN IF NOT EXISTS pulse_ai_classification VARCHAR(80) NOT NULL DEFAULT 'internal_project_document',
    ADD COLUMN IF NOT EXISTS pulse_ai_document_revision VARCHAR(120) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS pulse_ai_effective_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS pulse_ai_superseded_by_document_id UUID NULL REFERENCES project_intake_documents(project_intake_document_id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS pulse_ai_active_version_id UUID NULL,
    ADD COLUMN IF NOT EXISTS pulse_ai_processing_error_code VARCHAR(120) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS pulse_ai_processing_updated_at TIMESTAMPTZ NULL;

DO $pulse_ai_052_document_status_constraint$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_project_intake_documents_pulse_ai_processing_status'
    ) THEN
        ALTER TABLE project_intake_documents
            ADD CONSTRAINT ck_project_intake_documents_pulse_ai_processing_status
            CHECK (pulse_ai_processing_status IN (
                'not_requested', 'queued', 'scanning', 'extracting',
                'awaiting_ocr', 'embedding', 'indexing', 'ready',
                'retry_wait', 'failed', 'quarantined', 'cancelled', 'superseded'
            ));
    END IF;
END;
$pulse_ai_052_document_status_constraint$;

CREATE TABLE IF NOT EXISTS pulse_ai_document_processing_jobs (
    pulse_ai_document_processing_job_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_intake_document_id UUID NOT NULL REFERENCES project_intake_documents(project_intake_document_id) ON DELETE CASCADE,
    project_id UUID NULL REFERENCES projects(project_id) ON DELETE SET NULL,
    actual_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    effective_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    requested_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    requested_purpose VARCHAR(80) NOT NULL DEFAULT 'private_document_indexing',
    priority SMALLINT NOT NULL DEFAULT 50 CHECK (priority BETWEEN 1 AND 100),
    job_status VARCHAR(40) NOT NULL DEFAULT 'queued' CHECK (job_status IN (
        'queued', 'scanning', 'extracting', 'awaiting_ocr', 'embedding',
        'indexing', 'retry_wait', 'succeeded', 'failed', 'quarantined',
        'cancel_requested', 'cancelled'
    )),
    attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    maximum_attempts INTEGER NOT NULL DEFAULT 3 CHECK (maximum_attempts BETWEEN 1 AND 20),
    available_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    lease_owner VARCHAR(200) NOT NULL DEFAULT '',
    lease_expires_at TIMESTAMPTZ NULL,
    cancellation_requested BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id VARCHAR(160) NOT NULL DEFAULT '',
    source_sha256 VARCHAR(64) NOT NULL DEFAULT '',
    extraction_method VARCHAR(120) NOT NULL DEFAULT '',
    malware_scanner VARCHAR(120) NOT NULL DEFAULT '',
    malware_signature_version VARCHAR(160) NOT NULL DEFAULT '',
    ocr_provider VARCHAR(120) NOT NULL DEFAULT '',
    embedding_model VARCHAR(220) NOT NULL DEFAULT '',
    embedding_dimension INTEGER NULL CHECK (embedding_dimension IS NULL OR embedding_dimension > 0),
    index_provider VARCHAR(120) NOT NULL DEFAULT 'projectpulse_postgresql_hybrid',
    diagnostic_code VARCHAR(120) NOT NULL DEFAULT '',
    diagnostic_message TEXT NOT NULL DEFAULT '',
    metrics_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    requested_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    started_at TIMESTAMPTZ NULL,
    completed_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_pulse_ai_document_processing_jobs_active_document
    ON pulse_ai_document_processing_jobs(project_intake_document_id)
    WHERE job_status IN (
        'queued', 'scanning', 'extracting', 'awaiting_ocr',
        'embedding', 'indexing', 'retry_wait', 'cancel_requested'
    );
CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_processing_jobs_queue
    ON pulse_ai_document_processing_jobs(job_status, available_at, priority DESC, requested_at);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_processing_jobs_actor
    ON pulse_ai_document_processing_jobs(effective_user_id, requested_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_processing_jobs_project
    ON pulse_ai_document_processing_jobs(project_id, requested_at DESC);

CREATE TABLE IF NOT EXISTS pulse_ai_document_versions (
    pulse_ai_document_version_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_intake_document_id UUID NOT NULL REFERENCES project_intake_documents(project_intake_document_id) ON DELETE CASCADE,
    project_id UUID NULL REFERENCES projects(project_id) ON DELETE SET NULL,
    source_sha256 VARCHAR(64) NOT NULL,
    document_version VARCHAR(300) NOT NULL,
    document_revision VARCHAR(120) NOT NULL DEFAULT '',
    authority_status VARCHAR(40) NOT NULL DEFAULT 'candidate' CHECK (authority_status IN (
        'candidate', 'approved', 'canonical', 'superseded', 'rejected', 'revoked'
    )),
    classification VARCHAR(80) NOT NULL,
    extraction_method VARCHAR(120) NOT NULL,
    extraction_contract_version VARCHAR(120) NOT NULL,
    page_count INTEGER NOT NULL DEFAULT 0 CHECK (page_count >= 0),
    section_count INTEGER NOT NULL DEFAULT 0 CHECK (section_count >= 0),
    chunk_count INTEGER NOT NULL DEFAULT 0 CHECK (chunk_count >= 0),
    character_count INTEGER NOT NULL DEFAULT 0 CHECK (character_count >= 0),
    estimated_token_count INTEGER NOT NULL DEFAULT 0 CHECK (estimated_token_count >= 0),
    ocr_used BOOLEAN NOT NULL DEFAULT FALSE,
    malware_scanner VARCHAR(120) NOT NULL DEFAULT '',
    malware_signature_version VARCHAR(160) NOT NULL DEFAULT '',
    embedding_model VARCHAR(220) NOT NULL DEFAULT '',
    embedding_dimension INTEGER NULL CHECK (embedding_dimension IS NULL OR embedding_dimension > 0),
    index_provider VARCHAR(120) NOT NULL DEFAULT 'projectpulse_postgresql_hybrid',
    index_status VARCHAR(40) NOT NULL DEFAULT 'lexical_ready' CHECK (index_status IN (
        'lexical_ready', 'embedding_ready', 'ready', 'inactive', 'revoked', 'failed'
    )),
    effective_at TIMESTAMPTZ NULL,
    superseded_at TIMESTAMPTZ NULL,
    processed_by_job_id UUID NULL REFERENCES pulse_ai_document_processing_jobs(pulse_ai_document_processing_job_id) ON DELETE SET NULL,
    processed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(project_intake_document_id, source_sha256)
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_versions_document
    ON pulse_ai_document_versions(project_intake_document_id, processed_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_versions_project
    ON pulse_ai_document_versions(project_id, authority_status, processed_at DESC);
CREATE UNIQUE INDEX IF NOT EXISTS ux_pulse_ai_document_versions_one_canonical_category
    ON pulse_ai_document_versions(project_id, document_version)
    WHERE authority_status = 'canonical';

DO $pulse_ai_052_active_version_fk$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_project_intake_documents_pulse_ai_active_version'
    ) THEN
        ALTER TABLE project_intake_documents
            ADD CONSTRAINT fk_project_intake_documents_pulse_ai_active_version
            FOREIGN KEY (pulse_ai_active_version_id)
            REFERENCES pulse_ai_document_versions(pulse_ai_document_version_id)
            ON DELETE SET NULL;
    END IF;
END;
$pulse_ai_052_active_version_fk$;

CREATE TABLE IF NOT EXISTS pulse_ai_document_sections (
    pulse_ai_document_section_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pulse_ai_document_version_id UUID NOT NULL REFERENCES pulse_ai_document_versions(pulse_ai_document_version_id) ON DELETE CASCADE,
    project_intake_document_id UUID NOT NULL REFERENCES project_intake_documents(project_intake_document_id) ON DELETE CASCADE,
    section_index INTEGER NOT NULL CHECK (section_index >= 0),
    citation_anchor VARCHAR(500) NOT NULL,
    section_title TEXT NOT NULL DEFAULT '',
    page_number INTEGER NULL CHECK (page_number IS NULL OR page_number > 0),
    sheet_name VARCHAR(300) NULL,
    section_text TEXT NOT NULL,
    character_count INTEGER NOT NULL CHECK (character_count >= 0),
    text_sha256 VARCHAR(64) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(pulse_ai_document_version_id, section_index)
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_sections_version
    ON pulse_ai_document_sections(pulse_ai_document_version_id, section_index);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_sections_document
    ON pulse_ai_document_sections(project_intake_document_id, section_index);

CREATE TABLE IF NOT EXISTS pulse_ai_document_chunks (
    chunk_id VARCHAR(64) PRIMARY KEY,
    pulse_ai_document_version_id UUID NOT NULL REFERENCES pulse_ai_document_versions(pulse_ai_document_version_id) ON DELETE CASCADE,
    project_intake_document_id UUID NOT NULL REFERENCES project_intake_documents(project_intake_document_id) ON DELETE CASCADE,
    project_id UUID NULL REFERENCES projects(project_id) ON DELETE SET NULL,
    project_code VARCHAR(120) NOT NULL DEFAULT '',
    project_name TEXT NOT NULL DEFAULT '',
    customer_name TEXT NOT NULL DEFAULT '',
    document_category VARCHAR(80) NOT NULL DEFAULT 'other',
    document_version VARCHAR(300) NOT NULL,
    classification VARCHAR(80) NOT NULL,
    engineering_visible BOOLEAN NOT NULL DEFAULT TRUE,
    ai_timesheet_context_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    access_scope VARCHAR(120) NOT NULL,
    authorization_snapshot_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    chunk_index INTEGER NOT NULL CHECK (chunk_index >= 0),
    citation_anchor VARCHAR(500) NOT NULL,
    section_title TEXT NOT NULL DEFAULT '',
    page_number INTEGER NULL CHECK (page_number IS NULL OR page_number > 0),
    sheet_name VARCHAR(300) NULL,
    chunk_text TEXT NOT NULL,
    search_vector TSVECTOR GENERATED ALWAYS AS (
        to_tsvector('english',
            coalesce(project_code, '') || ' ' ||
            coalesce(project_name, '') || ' ' ||
            coalesce(customer_name, '') || ' ' ||
            coalesce(document_category, '') || ' ' ||
            coalesce(section_title, '') || ' ' ||
            coalesce(chunk_text, ''))
    ) STORED,
    source_sha256 VARCHAR(64) NOT NULL,
    text_sha256 VARCHAR(64) NOT NULL,
    character_count INTEGER NOT NULL CHECK (character_count >= 0),
    estimated_token_count INTEGER NOT NULL CHECK (estimated_token_count >= 0),
    embedding DOUBLE PRECISION[] NULL,
    embedding_dimension INTEGER NULL CHECK (embedding_dimension IS NULL OR embedding_dimension > 0),
    embedding_model VARCHAR(220) NOT NULL DEFAULT '',
    embedding_status VARCHAR(40) NOT NULL DEFAULT 'not_requested' CHECK (embedding_status IN (
        'not_requested', 'pending', 'ready', 'failed', 'revoked'
    )),
    index_status VARCHAR(40) NOT NULL DEFAULT 'lexical_ready' CHECK (index_status IN (
        'lexical_ready', 'embedding_ready', 'ready', 'inactive', 'revoked', 'failed'
    )),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    processed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(pulse_ai_document_version_id, chunk_index)
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_chunks_search
    ON pulse_ai_document_chunks USING GIN(search_vector);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_chunks_project
    ON pulse_ai_document_chunks(project_id, is_active, document_category, processed_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_chunks_timesheet
    ON pulse_ai_document_chunks(project_id, ai_timesheet_context_enabled, is_active, processed_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_chunks_document
    ON pulse_ai_document_chunks(project_intake_document_id, is_active, chunk_index);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_chunks_embedding_status
    ON pulse_ai_document_chunks(embedding_status, index_status, processed_at DESC);

CREATE TABLE IF NOT EXISTS pulse_ai_document_processing_events (
    pulse_ai_document_processing_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pulse_ai_document_processing_job_id UUID NULL REFERENCES pulse_ai_document_processing_jobs(pulse_ai_document_processing_job_id) ON DELETE SET NULL,
    project_intake_document_id UUID NULL REFERENCES project_intake_documents(project_intake_document_id) ON DELETE SET NULL,
    project_id UUID NULL REFERENCES projects(project_id) ON DELETE SET NULL,
    actual_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    effective_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    event_code VARCHAR(120) NOT NULL,
    event_status VARCHAR(40) NOT NULL CHECK (event_status IN (
        'requested', 'succeeded', 'partial', 'failed', 'blocked', 'cancelled', 'quarantined'
    )),
    correlation_id VARCHAR(160) NOT NULL DEFAULT '',
    diagnostic_code VARCHAR(120) NOT NULL DEFAULT '',
    evidence_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_processing_events_job
    ON pulse_ai_document_processing_events(pulse_ai_document_processing_job_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_processing_events_document
    ON pulse_ai_document_processing_events(project_intake_document_id, created_at DESC);

CREATE OR REPLACE FUNCTION pulse_ai_052_touch_job_updated_at()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $pulse_ai_052_touch_job_updated_at_body$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$pulse_ai_052_touch_job_updated_at_body$;

DROP TRIGGER IF EXISTS trg_pulse_ai_052_processing_jobs_updated_at
    ON pulse_ai_document_processing_jobs;
CREATE TRIGGER trg_pulse_ai_052_processing_jobs_updated_at
BEFORE UPDATE ON pulse_ai_document_processing_jobs
FOR EACH ROW EXECUTE FUNCTION pulse_ai_052_touch_job_updated_at();

CREATE OR REPLACE FUNCTION pulse_ai_052_block_processing_event_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $pulse_ai_052_processing_event_immutable$
BEGIN
    RAISE EXCEPTION 'Pulse AI document processing event evidence is immutable.';
END;
$pulse_ai_052_processing_event_immutable$;

DROP TRIGGER IF EXISTS trg_pulse_ai_052_processing_events_immutable
    ON pulse_ai_document_processing_events;
CREATE TRIGGER trg_pulse_ai_052_processing_events_immutable
BEFORE UPDATE OR DELETE ON pulse_ai_document_processing_events
FOR EACH ROW EXECUTE FUNCTION pulse_ai_052_block_processing_event_mutation();

UPDATE project_intake_documents
SET pulse_ai_classification = CASE
        WHEN LOWER(COALESCE(document_category, document_type, 'other')) IN (
            'sow', 'statement_of_work', 'gsd', 'global_solution_design',
            'contract', 'rate', 'pricing'
        ) THEN 'restricted_internal_document'
        WHEN LOWER(COALESCE(document_category, document_type, 'other')) IN (
            'architecture', 'design', 'order', 'quote', 'proposal'
        ) THEN 'confidential_project_document'
        ELSE 'internal_project_document'
    END,
    pulse_ai_processing_updated_at = COALESCE(pulse_ai_processing_updated_at, NOW());

INSERT INTO app_permissions (
    permission_code,
    permission_name,
    module_code,
    permission_description
)
VALUES
    ('VIEW_PULSE_AI_DOCUMENT_RUNTIME', 'View Pulse AI Document Runtime', '011', 'View private document processing readiness, authorized jobs, version evidence, and index health within the current role and project scope.'),
    ('QUEUE_PULSE_AI_DOCUMENT_PROCESSING', 'Queue Pulse AI Document Processing', '011', 'Queue an authorized project document for private malware scanning, extraction, OCR when approved, chunking, embeddings, and indexing.'),
    ('CANCEL_PULSE_AI_DOCUMENT_PROCESSING', 'Cancel Pulse AI Document Processing', '011', 'Request cancellation of a queued or running private document processing job.'),
    ('RETRY_PULSE_AI_DOCUMENT_PROCESSING', 'Retry Pulse AI Document Processing', '011', 'Retry a failed, quarantined, OCR-waiting, or embedding-waiting document processing job after the blocker is resolved.'),
    ('APPROVE_PULSE_AI_DOCUMENT_VERSION', 'Approve Pulse AI Document Version', '011', 'Designate an authorized document version as approved, canonical, superseded, rejected, or revoked through a separately controlled approval workflow.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

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
    'PULSE_AI_PRIVATE_DOCUMENT_RUNTIME',
    'Pulse AI Private Document Runtime',
    '011',
    '#work-task-builder',
    'VIEW_PULSE_AI_DOCUMENT_RUNTIME',
    'Durable private malware scanning, extraction, OCR coordination, citation-preserving chunks, private embeddings, permission-scoped hybrid indexing, version authority, and immutable processing evidence.',
    115,
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

-- Administrators receive all runtime capabilities.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code IN (
    'VIEW_PULSE_AI_DOCUMENT_RUNTIME',
    'QUEUE_PULSE_AI_DOCUMENT_PROCESSING',
    'CANCEL_PULSE_AI_DOCUMENT_PROCESSING',
    'RETRY_PULSE_AI_DOCUMENT_PROCESSING',
    'APPROVE_PULSE_AI_DOCUMENT_VERSION'
)
WHERE UPPER(role.role_code) IN ('SUPER_ADMINISTRATOR', 'ADMINISTRATOR')
ON CONFLICT DO NOTHING;

-- Project Team Coordinators can operate the private processing queue and approve
-- source authority without gaining access to documents outside their existing scope.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code IN (
    'VIEW_PULSE_AI_DOCUMENT_RUNTIME',
    'QUEUE_PULSE_AI_DOCUMENT_PROCESSING',
    'CANCEL_PULSE_AI_DOCUMENT_PROCESSING',
    'RETRY_PULSE_AI_DOCUMENT_PROCESSING',
    'APPROVE_PULSE_AI_DOCUMENT_VERSION'
)
WHERE UPPER(role.role_code) = 'PROJECT_TEAM_COORDINATOR'
ON CONFLICT DO NOTHING;

-- Project Management and Engineering leads can view scoped processing evidence.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code = 'VIEW_PULSE_AI_DOCUMENT_RUNTIME'
WHERE UPPER(role.role_code) IN (
    'PROJECT_MANAGER', 'PROJECT_MANAGEMENT', 'PROJECT_MANAGEMENT_LEAD',
    'PROJECT_MANAGEMENT_TEAM_LEAD', 'PM_TEAM_LEAD',
    'ENGINEERING_LEAD', 'ENGINEERING_TEAM_LEAD'
)
ON CONFLICT DO NOTHING;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '052_pulse_ai_private_document_runtime',
    'Pulse AI durable private document processing queue, versions, citation-preserving sections and chunks, private embeddings, permission-scoped hybrid index metadata, and immutable processing evidence',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
