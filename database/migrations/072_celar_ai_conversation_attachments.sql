-- Celar AI Module 011 -- private, conversation-scoped chat attachments.
--
-- This additive migration reuses the existing private document processing,
-- malware scanning, extraction/OCR, chunking, embedding, and citation runtime.
-- It does not enable a provider, expose document text to a public model, or
-- change the Module 064 route order.

BEGIN;

DO $celar_ai_072_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.project_intake_documents') IS NULL
       OR to_regclass('public.pulse_ai_conversations') IS NULL
       OR to_regclass('public.pulse_ai_conversation_messages') IS NULL
       OR to_regclass('public.pulse_ai_system_inquiry_runs') IS NULL
       OR to_regclass('public.pulse_ai_answer_runs') IS NULL
       OR to_regclass('public.pulse_ai_answer_citations') IS NULL
       OR to_regclass('public.pulse_ai_answer_feedback') IS NULL
       OR to_regclass('public.pulse_ai_retrieval_events') IS NULL
       OR to_regclass('public.pulse_ai_document_processing_jobs') IS NULL
       OR to_regclass('public.pulse_ai_document_versions') IS NULL
       OR to_regclass('public.pulse_ai_document_chunks') IS NULL THEN
        RAISE EXCEPTION 'Celar AI conversation attachments require migrations 052, 053, and 054.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id = '071_ai_runtime_production_hardening'
    ) THEN
        RAISE EXCEPTION 'Celar AI conversation attachments require migration 071 production hardening.';
    END IF;
END;
$celar_ai_072_prerequisites$;

ALTER TABLE project_intake_documents
    ALTER COLUMN project_intake_request_id DROP NOT NULL;

DO $celar_ai_072_document_owner_constraint$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_project_intake_documents_origin_owner'
          AND conrelid = 'public.project_intake_documents'::regclass
    ) THEN
        ALTER TABLE project_intake_documents
            ADD CONSTRAINT ck_project_intake_documents_origin_owner
            CHECK (
                project_intake_request_id IS NOT NULL
                OR (
                    COALESCE(upload_source, '') = 'celar_ai_chat_attachment'
                    AND uploaded_by_user_id IS NOT NULL
                )
            );
    END IF;
END;
$celar_ai_072_document_owner_constraint$;

CREATE TABLE IF NOT EXISTS pulse_ai_conversation_attachments (
    pulse_ai_conversation_attachment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pulse_ai_conversation_id UUID NOT NULL
        REFERENCES pulse_ai_conversations(pulse_ai_conversation_id) ON DELETE RESTRICT,
    project_intake_document_id UUID NOT NULL UNIQUE
        REFERENCES project_intake_documents(project_intake_document_id) ON DELETE CASCADE,
    uploaded_by_user_id UUID NOT NULL
        REFERENCES app_users(user_id) ON DELETE RESTRICT,
    correlation_id VARCHAR(160) NOT NULL DEFAULT '',
    retention_until TIMESTAMPTZ NOT NULL,
    revoked_at TIMESTAMPTZ NULL,
    revocation_reason VARCHAR(300) NOT NULL DEFAULT '',
    last_selected_at TIMESTAMPTZ NULL,
    storage_purged_at TIMESTAMPTZ NULL,
    purge_attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (purge_attempt_count >= 0),
    purge_last_attempt_at TIMESTAMPTZ NULL,
    purge_diagnostic_code VARCHAR(120) NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CHECK (retention_until > created_at),
    CHECK (revoked_at IS NULL OR revoked_at >= created_at),
    CHECK (storage_purged_at IS NULL OR storage_purged_at >= created_at)
);

-- Content-free tombstones preserve purge accountability after the source
-- document, extracted text, chunks, embeddings, jobs, and attachment row are
-- physically removed. Deliberately no foreign key can retain a conversation,
-- user, or private document beyond its governed lifecycle.
CREATE TABLE IF NOT EXISTS pulse_ai_conversation_attachment_purge_audit (
    pulse_ai_conversation_attachment_purge_audit_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pulse_ai_conversation_attachment_id UUID NOT NULL UNIQUE,
    pulse_ai_conversation_id UUID NOT NULL,
    project_intake_document_id UUID NOT NULL,
    uploaded_by_user_id UUID NOT NULL,
    correlation_id VARCHAR(160) NOT NULL DEFAULT '',
    purge_reason VARCHAR(300) NOT NULL DEFAULT '',
    retention_until TIMESTAMPTZ NOT NULL,
    revoked_at TIMESTAMPTZ NULL,
    storage_purged_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CHECK (storage_purged_at >= created_at)
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_attachment_purge_audit_user
    ON pulse_ai_conversation_attachment_purge_audit(uploaded_by_user_id, storage_purged_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_attachment_purge_audit_conversation
    ON pulse_ai_conversation_attachment_purge_audit(pulse_ai_conversation_id, storage_purged_at DESC);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_conversation_attachments_conversation
    ON pulse_ai_conversation_attachments(
        pulse_ai_conversation_id,
        revoked_at,
        created_at
    );
CREATE INDEX IF NOT EXISTS ix_pulse_ai_conversation_attachments_user
    ON pulse_ai_conversation_attachments(uploaded_by_user_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_conversation_attachments_retention
    ON pulse_ai_conversation_attachments(retention_until)
    WHERE revoked_at IS NULL;
CREATE INDEX IF NOT EXISTS ix_pulse_ai_conversation_attachments_pending_purge
    ON pulse_ai_conversation_attachments(retention_until, created_at)
    WHERE storage_purged_at IS NULL;

CREATE OR REPLACE FUNCTION pulse_ai_072_touch_attachment()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $pulse_ai_072_touch_attachment_body$
BEGIN
    IF NEW.pulse_ai_conversation_id <> OLD.pulse_ai_conversation_id
       OR NEW.project_intake_document_id <> OLD.project_intake_document_id
       OR NEW.uploaded_by_user_id IS DISTINCT FROM OLD.uploaded_by_user_id
       OR NEW.created_at <> OLD.created_at THEN
        RAISE EXCEPTION 'Celar AI attachment ownership evidence is immutable.';
    END IF;
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$pulse_ai_072_touch_attachment_body$;

DROP TRIGGER IF EXISTS trg_pulse_ai_072_attachment_updated_at
    ON pulse_ai_conversation_attachments;
CREATE TRIGGER trg_pulse_ai_072_attachment_updated_at
BEFORE UPDATE ON pulse_ai_conversation_attachments
FOR EACH ROW EXECUTE FUNCTION pulse_ai_072_touch_attachment();

CREATE OR REPLACE FUNCTION pulse_ai_072_block_conversation_owner_reassignment()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $pulse_ai_072_block_conversation_owner_reassignment_body$
BEGIN
    IF (
        NEW.actual_user_id IS DISTINCT FROM OLD.actual_user_id
        OR NEW.effective_user_id IS DISTINCT FROM OLD.effective_user_id
    ) AND EXISTS (
        SELECT 1
        FROM pulse_ai_conversation_attachments attachment
        WHERE attachment.pulse_ai_conversation_id = OLD.pulse_ai_conversation_id
    ) THEN
        RAISE EXCEPTION 'Celar AI conversation ownership cannot change while private attachments exist.';
    END IF;
    RETURN NEW;
END;
$pulse_ai_072_block_conversation_owner_reassignment_body$;

DROP TRIGGER IF EXISTS trg_pulse_ai_072_conversation_owner_immutable
    ON pulse_ai_conversations;
CREATE TRIGGER trg_pulse_ai_072_conversation_owner_immutable
BEFORE UPDATE OF actual_user_id, effective_user_id ON pulse_ai_conversations
FOR EACH ROW EXECUTE FUNCTION pulse_ai_072_block_conversation_owner_reassignment();

CREATE OR REPLACE FUNCTION pulse_ai_072_guard_chat_document_delete()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $pulse_ai_072_guard_chat_document_delete_body$
BEGIN
    IF COALESCE(OLD.upload_source, '') <> 'celar_ai_chat_attachment' THEN
        RETURN OLD;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pulse_ai_conversation_attachments attachment
        JOIN pulse_ai_conversation_attachment_purge_audit audit
          ON audit.pulse_ai_conversation_attachment_id = attachment.pulse_ai_conversation_attachment_id
         AND audit.pulse_ai_conversation_id = attachment.pulse_ai_conversation_id
         AND audit.project_intake_document_id = attachment.project_intake_document_id
         AND audit.uploaded_by_user_id = attachment.uploaded_by_user_id
        WHERE attachment.project_intake_document_id = OLD.project_intake_document_id
    ) THEN
        RAISE EXCEPTION 'Celar AI chat documents require a governed purge-audit tombstone before deletion.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM pulse_ai_answer_citations citation
        WHERE citation.project_intake_document_id = OLD.project_intake_document_id
    ) THEN
        RAISE EXCEPTION 'Celar AI chat document citations must be removed by governed retention before deletion.';
    END IF;

    RETURN OLD;
END;
$pulse_ai_072_guard_chat_document_delete_body$;

DROP TRIGGER IF EXISTS trg_pulse_ai_072_chat_document_delete_guard
    ON project_intake_documents;
CREATE TRIGGER trg_pulse_ai_072_chat_document_delete_guard
BEFORE DELETE ON project_intake_documents
FOR EACH ROW EXECUTE FUNCTION pulse_ai_072_guard_chat_document_delete();

-- Once governed retention redacts an attachment-derived answer run, no late
-- model completion or administrative code path may repopulate its mutable
-- question, answer, citation-summary, provider, or evidence fields. The
-- immutable retrieval event remains linked to this neutral audit row.
CREATE OR REPLACE FUNCTION pulse_ai_072_block_purged_answer_resurrection()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $pulse_ai_072_block_purged_answer_resurrection_body$
BEGIN
    IF OLD.diagnostic_code = 'private_attachment_retention_purged'
       AND (
           NEW.answer_status IS DISTINCT FROM OLD.answer_status
           OR NEW.project_id IS DISTINCT FROM OLD.project_id
           OR NEW.project_code IS DISTINCT FROM OLD.project_code
           OR NEW.question_text IS DISTINCT FROM OLD.question_text
           OR NEW.question_sha256 IS DISTINCT FROM OLD.question_sha256
           OR NEW.request_filters_json IS DISTINCT FROM OLD.request_filters_json
           OR NEW.private_model_provider IS DISTINCT FROM OLD.private_model_provider
           OR NEW.private_model_name IS DISTINCT FROM OLD.private_model_name
           OR NEW.retrieval_mode IS DISTINCT FROM OLD.retrieval_mode
           OR NEW.retrieved_chunk_count IS DISTINCT FROM OLD.retrieved_chunk_count
           OR NEW.cited_source_count IS DISTINCT FROM OLD.cited_source_count
           OR NEW.source_document_count IS DISTINCT FROM OLD.source_document_count
           OR NEW.source_version_count IS DISTINCT FROM OLD.source_version_count
           OR NEW.input_character_count IS DISTINCT FROM OLD.input_character_count
           OR NEW.output_character_count IS DISTINCT FROM OLD.output_character_count
           OR NEW.confidence_score IS DISTINCT FROM OLD.confidence_score
           OR NEW.coverage_score IS DISTINCT FROM OLD.coverage_score
           OR NEW.citation_coverage_score IS DISTINCT FROM OLD.citation_coverage_score
           OR NEW.answer_json IS DISTINCT FROM OLD.answer_json
           OR NEW.warning_codes IS DISTINCT FROM OLD.warning_codes
           OR NEW.missing_evidence IS DISTINCT FROM OLD.missing_evidence
           OR NEW.conflicts_json IS DISTINCT FROM OLD.conflicts_json
           OR NEW.source_health_json IS DISTINCT FROM OLD.source_health_json
           OR NEW.privacy_evidence_json IS DISTINCT FROM OLD.privacy_evidence_json
           OR NEW.diagnostic_code IS DISTINCT FROM OLD.diagnostic_code
           OR NEW.diagnostic_message IS DISTINCT FROM OLD.diagnostic_message
           OR NEW.data_as_of IS DISTINCT FROM OLD.data_as_of
       ) THEN
        RAISE EXCEPTION 'Purged Celar AI attachment answer content cannot be restored.';
    END IF;
    RETURN NEW;
END;
$pulse_ai_072_block_purged_answer_resurrection_body$;

DROP TRIGGER IF EXISTS trg_pulse_ai_072_purged_answer_immutable
    ON pulse_ai_answer_runs;
CREATE TRIGGER trg_pulse_ai_072_purged_answer_immutable
BEFORE UPDATE ON pulse_ai_answer_runs
FOR EACH ROW EXECUTE FUNCTION pulse_ai_072_block_purged_answer_resurrection();

-- Feedback may contain corrected answer text. Lock the owning answer run before
-- every feedback write so retention and feedback have a single serialization
-- point, and reject any write after the run has been purged.
CREATE OR REPLACE FUNCTION pulse_ai_072_guard_purged_answer_feedback()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $pulse_ai_072_guard_purged_answer_feedback_body$
DECLARE
    answer_diagnostic_code TEXT;
BEGIN
    SELECT COALESCE(answer_run.diagnostic_code, '')
    INTO answer_diagnostic_code
    FROM pulse_ai_answer_runs answer_run
    WHERE answer_run.pulse_ai_answer_run_id = NEW.pulse_ai_answer_run_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Celar AI feedback requires an existing answer run.';
    END IF;
    IF answer_diagnostic_code = 'private_attachment_retention_purged' THEN
        RAISE EXCEPTION 'Feedback cannot restore purged Celar AI attachment content.';
    END IF;
    RETURN NEW;
END;
$pulse_ai_072_guard_purged_answer_feedback_body$;

DROP TRIGGER IF EXISTS trg_pulse_ai_072_purged_answer_feedback_guard
    ON pulse_ai_answer_feedback;
CREATE TRIGGER trg_pulse_ai_072_purged_answer_feedback_guard
BEFORE INSERT OR UPDATE OF feedback_reason, corrected_answer_json, training_candidate, training_review_status
ON pulse_ai_answer_feedback
FOR EACH ROW EXECUTE FUNCTION pulse_ai_072_guard_purged_answer_feedback();

CREATE OR REPLACE FUNCTION pulse_ai_072_validate_attachment_ownership()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $pulse_ai_072_validate_attachment_ownership_body$
DECLARE
    target_document_id UUID;
    document_request_id UUID;
    document_project_id UUID;
    document_uploader_id UUID;
    document_upload_source TEXT;
    attachment_count INTEGER;
    valid_owner_count INTEGER;
BEGIN
    IF TG_TABLE_NAME = 'pulse_ai_conversation_attachments' THEN
        target_document_id := CASE
            WHEN TG_OP = 'DELETE' THEN OLD.project_intake_document_id
            ELSE NEW.project_intake_document_id
        END;
    ELSE
        target_document_id := CASE
            WHEN TG_OP = 'DELETE' THEN OLD.project_intake_document_id
            ELSE NEW.project_intake_document_id
        END;
    END IF;

    SELECT
        document.project_intake_request_id,
        document.project_id,
        document.uploaded_by_user_id,
        COALESCE(document.upload_source, '')
    INTO
        document_request_id,
        document_project_id,
        document_uploader_id,
        document_upload_source
    FROM project_intake_documents document
    WHERE document.project_intake_document_id = target_document_id;

    -- Document deletion cascades the attachment and is the governed purge path.
    IF NOT FOUND THEN
        IF TG_OP = 'DELETE' THEN
            RETURN OLD;
        END IF;
        RETURN NEW;
    END IF;

    SELECT
        COUNT(*)::integer,
        COUNT(*) FILTER (
            WHERE attachment.uploaded_by_user_id = document_uploader_id
              AND conversation.effective_user_id = document_uploader_id
              AND conversation.actual_user_id = document_uploader_id
        )::integer
    INTO attachment_count, valid_owner_count
    FROM pulse_ai_conversation_attachments attachment
    JOIN pulse_ai_conversations conversation
      ON conversation.pulse_ai_conversation_id = attachment.pulse_ai_conversation_id
    WHERE attachment.project_intake_document_id = target_document_id;

    IF document_upload_source = 'celar_ai_chat_attachment' THEN
        IF document_request_id IS NOT NULL
           OR document_project_id IS NOT NULL
           OR document_uploader_id IS NULL
           OR attachment_count <> 1
           OR valid_owner_count <> 1 THEN
            RAISE EXCEPTION 'Celar AI chat document ownership must match exactly one conversation attachment and its actual/effective owner.';
        END IF;
    ELSIF attachment_count <> 0 THEN
        RAISE EXCEPTION 'Celar AI conversation attachments may reference only chat-origin documents.';
    END IF;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$pulse_ai_072_validate_attachment_ownership_body$;

DROP TRIGGER IF EXISTS trg_pulse_ai_072_document_attachment_ownership
    ON project_intake_documents;
CREATE CONSTRAINT TRIGGER trg_pulse_ai_072_document_attachment_ownership
AFTER INSERT OR UPDATE OR DELETE ON project_intake_documents
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION pulse_ai_072_validate_attachment_ownership();

DROP TRIGGER IF EXISTS trg_pulse_ai_072_attachment_document_ownership
    ON pulse_ai_conversation_attachments;
CREATE CONSTRAINT TRIGGER trg_pulse_ai_072_attachment_document_ownership
AFTER INSERT OR UPDATE OR DELETE ON pulse_ai_conversation_attachments
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION pulse_ai_072_validate_attachment_ownership();

INSERT INTO app_permissions (
    permission_code,
    permission_name,
    module_code,
    permission_description
)
VALUES (
    'ATTACH_CELAR_AI_CHAT_DOCUMENTS',
    'Attach Documents to Celar AI Chat',
    '011',
    'Upload, select, list, and revoke private documents owned by the current user''s Celar AI conversation.'
)
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

-- Attachment authority follows the existing Ask Celar AI role assignment. The
-- effective-user and conversation-owner checks remain mandatory at every route.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT DISTINCT ask_role.app_role_id, attachment_permission.app_permission_id
FROM app_role_permissions ask_role
JOIN app_permissions ask_permission
  ON ask_permission.app_permission_id = ask_role.app_permission_id
 AND ask_permission.permission_code = 'ASK_PULSE_AI_SYSTEM_INTELLIGENCE'
CROSS JOIN app_permissions attachment_permission
WHERE attachment_permission.permission_code = 'ATTACH_CELAR_AI_CHAT_DOCUMENTS'
ON CONFLICT DO NOTHING;

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
    'CELAR_AI_CHAT_ATTACHMENTS',
    'Celar AI Private Chat Attachments',
    '011',
    '#celar-ai',
    'ATTACH_CELAR_AI_CHAT_DOCUMENTS',
    'Conversation-owned private document upload, processing, selection, citation, retention, and revocation for Ask Celar AI.',
    1155,
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

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '072_celar_ai_conversation_attachments',
    'Module 011 private conversation attachments using the hardened Celar AI document runtime',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
