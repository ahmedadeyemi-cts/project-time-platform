-- Pulse AI Module 011 — phase 011C
-- Durable private document processing, encrypted artifact metadata, audited jobs,
-- version authority, permission-scoped index receipts, revocation, and role policy.
--
-- This migration stores metadata and encrypted-artifact references only. It does
-- not store plaintext document sections, plaintext chunks, provider secrets, or
-- embedding vectors in the transactional database.

BEGIN;

CREATE TABLE IF NOT EXISTS pulse_ai_document_versions (
    pulse_ai_document_version_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_intake_document_id UUID NOT NULL REFERENCES project_intake_documents(project_intake_document_id) ON DELETE CASCADE,
    project_id UUID NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    source_sha256 CHAR(64) NOT NULL,
    original_file_name TEXT NOT NULL,
    document_category VARCHAR(80) NOT NULL DEFAULT 'other',
    classification VARCHAR(40) NOT NULL DEFAULT 'restricted' CHECK (classification IN (
        'public', 'internal', 'confidential', 'restricted'
    )),
    document_version_label VARCHAR(160) NOT NULL,
    version_state VARCHAR(30) NOT NULL DEFAULT 'draft' CHECK (version_state IN (
        'draft', 'processing', 'review_required', 'approved', 'superseded', 'revoked', 'failed'
    )),
    canonical_for_category BOOLEAN NOT NULL DEFAULT FALSE,
    effective_at TIMESTAMPTZ NULL,
    approved_at TIMESTAMPTZ NULL,
    approved_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    superseded_at TIMESTAMPTZ NULL,
    superseded_by_version_id UUID NULL,
    revoked_at TIMESTAMPTZ NULL,
    revoked_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    revocation_reason TEXT NOT NULL DEFAULT '',
    extraction_artifact_sha256 CHAR(64) NULL,
    context_summary_status VARCHAR(30) NOT NULL DEFAULT 'not_generated' CHECK (context_summary_status IN (
        'not_generated', 'queued', 'generated', 'review_required', 'approved', 'failed', 'revoked'
    )),
    created_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(project_intake_document_id, source_sha256)
);

DO $pulse_ai_052_version_fk$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_pulse_ai_document_versions_superseded_by'
          AND conrelid = 'pulse_ai_document_versions'::regclass
    ) THEN
        ALTER TABLE pulse_ai_document_versions
        ADD CONSTRAINT fk_pulse_ai_document_versions_superseded_by
        FOREIGN KEY (superseded_by_version_id)
        REFERENCES pulse_ai_document_versions(pulse_ai_document_version_id)
        ON DELETE SET NULL;
    END IF;
END;
$pulse_ai_052_version_fk$;

CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_versions_document
    ON pulse_ai_document_versions(project_intake_document_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_versions_project
    ON pulse_ai_document_versions(project_id, document_category, version_state, created_at DESC);
CREATE UNIQUE INDEX IF NOT EXISTS ux_pulse_ai_document_versions_canonical
    ON pulse_ai_document_versions(project_id, lower(document_category))
    WHERE canonical_for_category = TRUE
      AND version_state = 'approved'
      AND revoked_at IS NULL;

CREATE TABLE IF NOT EXISTS pulse_ai_document_scan_results (
    pulse_ai_document_scan_result_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pulse_ai_document_version_id UUID NOT NULL REFERENCES pulse_ai_document_versions(pulse_ai_document_version_id) ON DELETE CASCADE,
    scanner_code VARCHAR(100) NOT NULL,
    scanner_version VARCHAR(120) NOT NULL DEFAULT '',
    signature_version VARCHAR(160) NOT NULL DEFAULT '',
    source_sha256 CHAR(64) NOT NULL,
    scan_status VARCHAR(30) NOT NULL CHECK (scan_status IN (
        'clean', 'infected', 'error', 'unavailable', 'mismatch'
    )),
    threat_name VARCHAR(240) NOT NULL DEFAULT '',
    diagnostic_code VARCHAR(120) NOT NULL DEFAULT '',
    diagnostic_message TEXT NOT NULL DEFAULT '',
    correlation_id VARCHAR(160) NOT NULL DEFAULT '',
    scanned_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_scan_results_version
    ON pulse_ai_document_scan_results(pulse_ai_document_version_id, scanned_at DESC);

CREATE TABLE IF NOT EXISTS pulse_ai_document_processing_jobs (
    pulse_ai_document_processing_job_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pulse_ai_document_version_id UUID NOT NULL REFERENCES pulse_ai_document_versions(pulse_ai_document_version_id) ON DELETE CASCADE,
    project_intake_document_id UUID NOT NULL REFERENCES project_intake_documents(project_intake_document_id) ON DELETE CASCADE,
    project_id UUID NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    requested_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    actual_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    effective_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    request_reason TEXT NOT NULL DEFAULT '',
    priority SMALLINT NOT NULL DEFAULT 50 CHECK (priority BETWEEN 1 AND 100),
    job_state VARCHAR(40) NOT NULL DEFAULT 'queued' CHECK (job_state IN (
        'queued', 'leased', 'scanning', 'extracting', 'ocr', 'chunking', 'embedding',
        'indexing', 'review_required', 'completed', 'blocked', 'failed', 'cancelled', 'revoked'
    )),
    current_stage VARCHAR(80) NOT NULL DEFAULT 'queued',
    attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    maximum_attempts INTEGER NOT NULL DEFAULT 3 CHECK (maximum_attempts BETWEEN 1 AND 20),
    next_attempt_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    lease_owner VARCHAR(160) NOT NULL DEFAULT '',
    lease_token UUID NULL,
    lease_expires_at TIMESTAMPTZ NULL,
    correlation_id VARCHAR(160) NOT NULL DEFAULT '',
    configuration_snapshot JSONB NOT NULL DEFAULT '{}'::JSONB,
    source_sha256 CHAR(64) NOT NULL,
    section_count INTEGER NOT NULL DEFAULT 0 CHECK (section_count >= 0),
    chunk_count INTEGER NOT NULL DEFAULT 0 CHECK (chunk_count >= 0),
    indexed_chunk_count INTEGER NOT NULL DEFAULT 0 CHECK (indexed_chunk_count >= 0),
    diagnostic_code VARCHAR(120) NOT NULL DEFAULT '',
    diagnostic_message TEXT NOT NULL DEFAULT '',
    queued_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    started_at TIMESTAMPTZ NULL,
    completed_at TIMESTAMPTZ NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_processing_jobs_queue
    ON pulse_ai_document_processing_jobs(job_state, priority DESC, next_attempt_at, queued_at);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_processing_jobs_document
    ON pulse_ai_document_processing_jobs(project_intake_document_id, queued_at DESC);
CREATE UNIQUE INDEX IF NOT EXISTS ux_pulse_ai_document_processing_jobs_active_version
    ON pulse_ai_document_processing_jobs(pulse_ai_document_version_id)
    WHERE job_state IN (
        'queued', 'leased', 'scanning', 'extracting', 'ocr', 'chunking', 'embedding', 'indexing'
    );

CREATE TABLE IF NOT EXISTS pulse_ai_document_artifacts (
    pulse_ai_document_artifact_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pulse_ai_document_version_id UUID NOT NULL REFERENCES pulse_ai_document_versions(pulse_ai_document_version_id) ON DELETE CASCADE,
    pulse_ai_document_processing_job_id UUID NULL REFERENCES pulse_ai_document_processing_jobs(pulse_ai_document_processing_job_id) ON DELETE SET NULL,
    artifact_kind VARCHAR(60) NOT NULL CHECK (artifact_kind IN (
        'extraction_manifest', 'ocr_manifest', 'section_payload', 'chunk_payload',
        'embedding_receipt', 'index_receipt', 'context_summary_payload'
    )),
    storage_uri TEXT NOT NULL,
    artifact_sha256 CHAR(64) NOT NULL,
    encryption_algorithm VARCHAR(60) NOT NULL DEFAULT 'AES-256-GCM',
    encryption_key_version VARCHAR(120) NOT NULL,
    content_length_bytes BIGINT NOT NULL DEFAULT 0 CHECK (content_length_bytes >= 0),
    retention_until TIMESTAMPTZ NULL,
    revoked_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(pulse_ai_document_version_id, artifact_kind, artifact_sha256)
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_artifacts_version
    ON pulse_ai_document_artifacts(pulse_ai_document_version_id, artifact_kind, created_at DESC);

CREATE TABLE IF NOT EXISTS pulse_ai_document_sections (
    pulse_ai_document_section_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pulse_ai_document_version_id UUID NOT NULL REFERENCES pulse_ai_document_versions(pulse_ai_document_version_id) ON DELETE CASCADE,
    pulse_ai_document_artifact_id UUID NOT NULL REFERENCES pulse_ai_document_artifacts(pulse_ai_document_artifact_id) ON DELETE CASCADE,
    section_index INTEGER NOT NULL CHECK (section_index >= 0),
    citation_anchor VARCHAR(320) NOT NULL,
    section_title TEXT NOT NULL DEFAULT '',
    page_number INTEGER NULL CHECK (page_number IS NULL OR page_number > 0),
    sheet_name TEXT NULL,
    character_count INTEGER NOT NULL DEFAULT 0 CHECK (character_count >= 0),
    token_estimate INTEGER NOT NULL DEFAULT 0 CHECK (token_estimate >= 0),
    text_sha256 CHAR(64) NOT NULL,
    artifact_ordinal INTEGER NOT NULL CHECK (artifact_ordinal >= 0),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(pulse_ai_document_version_id, section_index)
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_sections_anchor
    ON pulse_ai_document_sections(pulse_ai_document_version_id, citation_anchor);

CREATE TABLE IF NOT EXISTS pulse_ai_document_chunks (
    pulse_ai_document_chunk_id VARCHAR(128) PRIMARY KEY,
    pulse_ai_document_version_id UUID NOT NULL REFERENCES pulse_ai_document_versions(pulse_ai_document_version_id) ON DELETE CASCADE,
    pulse_ai_document_section_id UUID NULL REFERENCES pulse_ai_document_sections(pulse_ai_document_section_id) ON DELETE SET NULL,
    pulse_ai_document_artifact_id UUID NOT NULL REFERENCES pulse_ai_document_artifacts(pulse_ai_document_artifact_id) ON DELETE CASCADE,
    project_id UUID NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    project_code VARCHAR(120) NOT NULL DEFAULT '',
    customer_scope VARCHAR(320) NOT NULL DEFAULT '',
    document_category VARCHAR(80) NOT NULL,
    document_version_label VARCHAR(160) NOT NULL,
    classification VARCHAR(40) NOT NULL,
    engineering_visible BOOLEAN NOT NULL,
    ai_timesheet_context_enabled BOOLEAN NOT NULL,
    access_scope VARCHAR(120) NOT NULL,
    security_metadata JSONB NOT NULL DEFAULT '{}'::JSONB,
    citation_anchor VARCHAR(320) NOT NULL,
    page_number INTEGER NULL CHECK (page_number IS NULL OR page_number > 0),
    sheet_name TEXT NULL,
    chunk_index INTEGER NOT NULL CHECK (chunk_index >= 0),
    character_count INTEGER NOT NULL DEFAULT 0 CHECK (character_count >= 0),
    token_estimate INTEGER NOT NULL DEFAULT 0 CHECK (token_estimate >= 0),
    source_sha256 CHAR(64) NOT NULL,
    text_sha256 CHAR(64) NOT NULL,
    artifact_ordinal INTEGER NOT NULL CHECK (artifact_ordinal >= 0),
    embedding_status VARCHAR(30) NOT NULL DEFAULT 'not_requested' CHECK (embedding_status IN (
        'not_requested', 'queued', 'generated', 'failed', 'revoked'
    )),
    embedding_model VARCHAR(200) NOT NULL DEFAULT '',
    embedding_dimension INTEGER NOT NULL DEFAULT 0 CHECK (embedding_dimension >= 0),
    index_status VARCHAR(30) NOT NULL DEFAULT 'not_indexed' CHECK (index_status IN (
        'not_indexed', 'queued', 'indexed', 'failed', 'revoked'
    )),
    index_provider VARCHAR(100) NOT NULL DEFAULT '',
    index_name VARCHAR(160) NOT NULL DEFAULT '',
    index_external_key VARCHAR(320) NOT NULL DEFAULT '',
    indexed_at TIMESTAMPTZ NULL,
    revoked_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(pulse_ai_document_version_id, chunk_index)
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_chunks_project_scope
    ON pulse_ai_document_chunks(project_id, document_category, index_status, revoked_at);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_chunks_version
    ON pulse_ai_document_chunks(pulse_ai_document_version_id, chunk_index);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_chunks_text_hash
    ON pulse_ai_document_chunks(text_sha256);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_chunks_security_metadata
    ON pulse_ai_document_chunks USING GIN (security_metadata);

CREATE TABLE IF NOT EXISTS pulse_ai_document_index_receipts (
    pulse_ai_document_index_receipt_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pulse_ai_document_chunk_id VARCHAR(128) NOT NULL REFERENCES pulse_ai_document_chunks(pulse_ai_document_chunk_id) ON DELETE CASCADE,
    index_provider VARCHAR(100) NOT NULL,
    index_name VARCHAR(160) NOT NULL,
    index_version VARCHAR(120) NOT NULL DEFAULT '',
    external_key VARCHAR(320) NOT NULL,
    receipt_status VARCHAR(30) NOT NULL CHECK (receipt_status IN (
        'indexed', 'deleted', 'failed', 'pending'
    )),
    diagnostic_code VARCHAR(120) NOT NULL DEFAULT '',
    diagnostic_message TEXT NOT NULL DEFAULT '',
    provider_receipt JSONB NOT NULL DEFAULT '{}'::JSONB,
    correlation_id VARCHAR(160) NOT NULL DEFAULT '',
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_index_receipts_chunk
    ON pulse_ai_document_index_receipts(pulse_ai_document_chunk_id, recorded_at DESC);

CREATE TABLE IF NOT EXISTS pulse_ai_document_access_snapshots (
    pulse_ai_document_access_snapshot_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pulse_ai_document_version_id UUID NOT NULL REFERENCES pulse_ai_document_versions(pulse_ai_document_version_id) ON DELETE CASCADE,
    policy_version VARCHAR(120) NOT NULL,
    access_scope VARCHAR(120) NOT NULL,
    access_scope_sha256 CHAR(64) NOT NULL,
    access_metadata JSONB NOT NULL DEFAULT '{}'::JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_access_snapshots_version
    ON pulse_ai_document_access_snapshots(pulse_ai_document_version_id, created_at DESC);

CREATE TABLE IF NOT EXISTS pulse_ai_document_revocations (
    pulse_ai_document_revocation_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pulse_ai_document_version_id UUID NOT NULL REFERENCES pulse_ai_document_versions(pulse_ai_document_version_id) ON DELETE CASCADE,
    requested_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    reason TEXT NOT NULL,
    revocation_status VARCHAR(30) NOT NULL DEFAULT 'requested' CHECK (revocation_status IN (
        'requested', 'index_delete_pending', 'completed', 'failed'
    )),
    requested_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ NULL,
    diagnostic_code VARCHAR(120) NOT NULL DEFAULT '',
    diagnostic_message TEXT NOT NULL DEFAULT ''
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_revocations_version
    ON pulse_ai_document_revocations(pulse_ai_document_version_id, requested_at DESC);

CREATE TABLE IF NOT EXISTS pulse_ai_document_processing_events (
    pulse_ai_document_processing_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pulse_ai_document_processing_job_id UUID NULL REFERENCES pulse_ai_document_processing_jobs(pulse_ai_document_processing_job_id) ON DELETE SET NULL,
    pulse_ai_document_version_id UUID NULL REFERENCES pulse_ai_document_versions(pulse_ai_document_version_id) ON DELETE SET NULL,
    project_intake_document_id UUID NULL REFERENCES project_intake_documents(project_intake_document_id) ON DELETE SET NULL,
    event_code VARCHAR(120) NOT NULL,
    event_status VARCHAR(40) NOT NULL CHECK (event_status IN (
        'requested', 'started', 'succeeded', 'partial', 'blocked', 'failed', 'cancelled', 'revoked'
    )),
    actor_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    correlation_id VARCHAR(160) NOT NULL DEFAULT '',
    diagnostic_code VARCHAR(120) NOT NULL DEFAULT '',
    diagnostic_message TEXT NOT NULL DEFAULT '',
    evidence_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_processing_events_job
    ON pulse_ai_document_processing_events(pulse_ai_document_processing_job_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_document_processing_events_document
    ON pulse_ai_document_processing_events(project_intake_document_id, created_at DESC);

CREATE OR REPLACE FUNCTION projectpulse052_block_private_ai_evidence_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse052_immutable$
BEGIN
    RAISE EXCEPTION 'Pulse AI private processing evidence is immutable.';
END;
$projectpulse052_immutable$;

DROP TRIGGER IF EXISTS trg_projectpulse052_scan_results_immutable
    ON pulse_ai_document_scan_results;
CREATE TRIGGER trg_projectpulse052_scan_results_immutable
BEFORE UPDATE OR DELETE ON pulse_ai_document_scan_results
FOR EACH ROW EXECUTE FUNCTION projectpulse052_block_private_ai_evidence_mutation();

DROP TRIGGER IF EXISTS trg_projectpulse052_processing_events_immutable
    ON pulse_ai_document_processing_events;
CREATE TRIGGER trg_projectpulse052_processing_events_immutable
BEFORE UPDATE OR DELETE ON pulse_ai_document_processing_events
FOR EACH ROW EXECUTE FUNCTION projectpulse052_block_private_ai_evidence_mutation();

ALTER TABLE project_intake_documents
    ADD COLUMN IF NOT EXISTS pulse_ai_processing_status VARCHAR(40) NOT NULL DEFAULT 'not_queued',
    ADD COLUMN IF NOT EXISTS pulse_ai_index_status VARCHAR(30) NOT NULL DEFAULT 'not_indexed',
    ADD COLUMN IF NOT EXISTS pulse_ai_source_sha256 CHAR(64) NULL,
    ADD COLUMN IF NOT EXISTS pulse_ai_active_version_id UUID NULL,
    ADD COLUMN IF NOT EXISTS pulse_ai_last_job_id UUID NULL,
    ADD COLUMN IF NOT EXISTS pulse_ai_last_processed_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS pulse_ai_canonical_version_label VARCHAR(160) NOT NULL DEFAULT '';

DO $pulse_ai_052_document_fks$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_project_intake_documents_pulse_ai_active_version'
          AND conrelid = 'project_intake_documents'::regclass
    ) THEN
        ALTER TABLE project_intake_documents
        ADD CONSTRAINT fk_project_intake_documents_pulse_ai_active_version
        FOREIGN KEY (pulse_ai_active_version_id)
        REFERENCES pulse_ai_document_versions(pulse_ai_document_version_id)
        ON DELETE SET NULL;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_project_intake_documents_pulse_ai_last_job'
          AND conrelid = 'project_intake_documents'::regclass
    ) THEN
        ALTER TABLE project_intake_documents
        ADD CONSTRAINT fk_project_intake_documents_pulse_ai_last_job
        FOREIGN KEY (pulse_ai_last_job_id)
        REFERENCES pulse_ai_document_processing_jobs(pulse_ai_document_processing_job_id)
        ON DELETE SET NULL;
    END IF;
END;
$pulse_ai_052_document_fks$;

CREATE INDEX IF NOT EXISTS ix_project_intake_documents_pulse_ai_processing
    ON project_intake_documents(pulse_ai_processing_status, pulse_ai_index_status, is_active);

INSERT INTO app_permissions (
    permission_code,
    permission_name,
    module_code,
    permission_description
)
VALUES
    ('VIEW_PULSE_AI_DOCUMENT_RUNTIME', 'View Pulse AI Document Runtime', '011', 'View private processing readiness, authorized document inventory, job status, version evidence, and retrieval readiness.'),
    ('MANAGE_PULSE_AI_DOCUMENT_PROCESSING', 'Manage Pulse AI Document Processing', '011', 'Queue, retry, cancel, and monitor private document processing for authorized project documents.'),
    ('APPROVE_PULSE_AI_DOCUMENT_VERSION', 'Approve Pulse AI Document Version', '011', 'Approve or supersede the authoritative SOW, GSD, or supporting-document version used by Pulse AI.'),
    ('REVOKE_PULSE_AI_DOCUMENT_INDEX', 'Revoke Pulse AI Document Index', '011', 'Revoke a document version and remove its chunks from private retrieval.'),
    ('VIEW_PULSE_AI_DOCUMENT_AUDIT', 'View Pulse AI Document Audit', '011', 'View immutable scan, processing, indexing, and revocation evidence.')
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
    'Durable private scanning, extraction, OCR, chunking, embedding, indexing, version authority, revocation, and audit evidence.',
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

-- Super Administrators, Administrators, and Project Team Coordinators own the
-- complete private processing lifecycle.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
CROSS JOIN app_permissions permission
WHERE upper(role.role_code) IN (
    'SUPER_ADMINISTRATOR', 'SYSTEM_ADMINISTRATOR', 'ADMINISTRATOR',
    'PROJECT_TEAM_COORDINATOR'
)
  AND permission.permission_code IN (
    'VIEW_PULSE_AI_DOCUMENT_RUNTIME',
    'MANAGE_PULSE_AI_DOCUMENT_PROCESSING',
    'APPROVE_PULSE_AI_DOCUMENT_VERSION',
    'REVOKE_PULSE_AI_DOCUMENT_INDEX',
    'VIEW_PULSE_AI_DOCUMENT_AUDIT'
  )
ON CONFLICT DO NOTHING;

-- Project Managers can process and review documents only within projects they
-- manage. Runtime authorization continues to enforce project scope.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code IN (
    'VIEW_PULSE_AI_DOCUMENT_RUNTIME',
    'MANAGE_PULSE_AI_DOCUMENT_PROCESSING',
    'APPROVE_PULSE_AI_DOCUMENT_VERSION',
    'VIEW_PULSE_AI_DOCUMENT_AUDIT'
)
WHERE upper(role.role_code) IN (
    'PROJECT_MANAGER', 'PROJECT_MANAGEMENT',
    'PROJECT_MANAGEMENT_LEAD', 'PROJECT_MANAGEMENT_TEAM_LEAD', 'PM_TEAM_LEAD'
)
ON CONFLICT DO NOTHING;

-- Engineering and Solution Architecture receive scoped read visibility only.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code = 'VIEW_PULSE_AI_DOCUMENT_RUNTIME'
WHERE upper(role.role_code) IN (
    'ENGINEERING', 'ENGINEER', 'ENGINEERING_LEAD', 'ENGINEERING_TEAM_LEAD',
    'SOLUTION_ARCHITECT', 'SALES_ENGINEERING'
)
ON CONFLICT DO NOTHING;

-- Executives may review organization-level readiness and audit evidence but do
-- not receive processing or version-mutation authority.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code IN (
    'VIEW_PULSE_AI_DOCUMENT_RUNTIME',
    'VIEW_PULSE_AI_DOCUMENT_AUDIT'
)
WHERE upper(role.role_code) IN ('EXECUTIVE', 'EXECUTIVE_LEADERSHIP')
ON CONFLICT DO NOTHING;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '052_pulse_ai_private_document_runtime',
    'Pulse AI durable private document versions, scan evidence, processing jobs, encrypted artifacts, sections, chunks, index receipts, revocation, audit, and role policy',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
