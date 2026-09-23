-- Migration 125: universal private document admission and durable Module 064
-- classification jobs. Admission is security-owned and is deliberately
-- independent from engineering visibility, AI-indexing consent, and retrieval
-- authorization. Laya remains a separate, review-required projection.
BEGIN;

DO $automatic_document_admission_prerequisites$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id = '052_pulse_ai_private_document_runtime'
    ) THEN
        RAISE EXCEPTION 'Migration 125 requires the private document runtime migration 052.';
    END IF;
END;
$automatic_document_admission_prerequisites$;

ALTER TABLE project_intake_documents
    ADD COLUMN IF NOT EXISTS pulse_ai_laya_classification_status VARCHAR(40) NOT NULL DEFAULT 'not_requested',
    ADD COLUMN IF NOT EXISTS pulse_ai_laya_policy_version BIGINT NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS pulse_ai_laya_model_revision VARCHAR(160) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS pulse_ai_laya_error_code VARCHAR(120) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS pulse_ai_laya_updated_at TIMESTAMPTZ NULL;

DO $automatic_document_admission_status_constraints$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_project_intake_documents_laya_classification_status'
          AND conrelid = 'public.project_intake_documents'::regclass
    ) THEN
        ALTER TABLE project_intake_documents
            ADD CONSTRAINT ck_project_intake_documents_laya_classification_status
            CHECK (pulse_ai_laya_classification_status IN (
                'not_requested', 'queued', 'running', 'succeeded',
                'retry_wait', 'failed', 'paused', 'cancelled'
            ));
    END IF;
END;
$automatic_document_admission_status_constraints$;

CREATE TABLE IF NOT EXISTS pulse_ai_laya_classification_jobs (
    pulse_ai_laya_classification_job_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_intake_document_id UUID NOT NULL
        REFERENCES project_intake_documents(project_intake_document_id) ON DELETE CASCADE,
    project_id UUID NULL REFERENCES projects(project_id) ON DELETE SET NULL,
    document_version_id UUID NULL
        REFERENCES pulse_ai_document_versions(pulse_ai_document_version_id) ON DELETE SET NULL,
    source_sha256 VARCHAR(64) NOT NULL CHECK (source_sha256 ~ '^[0-9a-f]{64}$'),
    policy_version BIGINT NOT NULL CHECK (policy_version > 0),
    model_revision VARCHAR(160) NOT NULL,
    service_principal_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    job_status VARCHAR(40) NOT NULL DEFAULT 'queued' CHECK (job_status IN (
        'queued', 'running', 'retry_wait', 'succeeded', 'failed', 'paused', 'cancelled'
    )),
    attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    maximum_attempts INTEGER NOT NULL DEFAULT 3 CHECK (maximum_attempts BETWEEN 1 AND 10),
    available_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    lease_owner VARCHAR(200) NOT NULL DEFAULT '',
    lease_token UUID NULL,
    lease_generation BIGINT NOT NULL DEFAULT 0 CHECK (lease_generation >= 0),
    lease_heartbeat_at TIMESTAMPTZ NULL,
    lease_expires_at TIMESTAMPTZ NULL,
    correlation_id VARCHAR(160) NOT NULL DEFAULT '',
    request_id UUID NOT NULL,
    diagnostic_code VARCHAR(120) NOT NULL DEFAULT '',
    diagnostic_message TEXT NOT NULL DEFAULT '',
    evidence_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    requested_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    started_at TIMESTAMPTZ NULL,
    completed_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(request_id)
);

-- One document/version/policy/model identity has one durable outcome. A
-- succeeded row must not be recreated by a worker restart; a failed row is
-- retried only by a new policy/model identity or an explicit future repair.
DROP INDEX IF EXISTS ux_pulse_ai_laya_classification_jobs_active_identity;
CREATE UNIQUE INDEX IF NOT EXISTS ux_pulse_ai_laya_classification_jobs_identity
    ON pulse_ai_laya_classification_jobs(
        project_intake_document_id, document_version_id, policy_version, model_revision);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_laya_classification_jobs_queue
    ON pulse_ai_laya_classification_jobs(job_status, available_at, requested_at);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_laya_classification_jobs_document
    ON pulse_ai_laya_classification_jobs(project_intake_document_id, requested_at DESC);

CREATE OR REPLACE FUNCTION pulse_ai_125_touch_laya_job_updated_at()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $pulse_ai_125_touch_laya_job_updated_at$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$pulse_ai_125_touch_laya_job_updated_at$;

DROP TRIGGER IF EXISTS trg_pulse_ai_125_laya_job_updated_at
    ON pulse_ai_laya_classification_jobs;
CREATE TRIGGER trg_pulse_ai_125_laya_job_updated_at
BEFORE UPDATE ON pulse_ai_laya_classification_jobs
FOR EACH ROW EXECUTE FUNCTION pulse_ai_125_touch_laya_job_updated_at();

INSERT INTO schema_migrations(migration_id, description, applied_at)
VALUES (
    '125_automatic_document_admission_laya',
    'Universal security admission and durable policy-bound Module 064 classification jobs',
    NOW())
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
