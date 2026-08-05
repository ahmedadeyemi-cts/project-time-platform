-- Guarded rollback for migration 072. Uploaded attachment evidence must be
-- explicitly removed through the governed retention/revocation process first.

BEGIN;

DO $celar_ai_072_rollback_guard$
BEGIN
    IF to_regclass('public.pulse_ai_conversation_attachments') IS NOT NULL
       AND EXISTS (SELECT 1 FROM pulse_ai_conversation_attachments LIMIT 1) THEN
        RAISE EXCEPTION 'Refusing migration 072 rollback while Celar AI conversation attachment records remain.';
    END IF;
    IF to_regclass('public.pulse_ai_conversation_attachment_purge_audit') IS NOT NULL
       AND EXISTS (SELECT 1 FROM pulse_ai_conversation_attachment_purge_audit LIMIT 1) THEN
        RAISE EXCEPTION 'Refusing migration 072 rollback while Celar AI attachment purge-audit records remain.';
    END IF;
END;
$celar_ai_072_rollback_guard$;

DROP TRIGGER IF EXISTS trg_pulse_ai_072_document_attachment_ownership
    ON project_intake_documents;
DROP TRIGGER IF EXISTS trg_pulse_ai_072_attachment_document_ownership
    ON pulse_ai_conversation_attachments;
DROP TRIGGER IF EXISTS trg_pulse_ai_072_attachment_updated_at
    ON pulse_ai_conversation_attachments;
DROP TRIGGER IF EXISTS trg_pulse_ai_072_conversation_owner_immutable
    ON pulse_ai_conversations;
DROP TRIGGER IF EXISTS trg_pulse_ai_072_chat_document_delete_guard
    ON project_intake_documents;
DROP TRIGGER IF EXISTS trg_pulse_ai_072_purged_answer_immutable
    ON pulse_ai_answer_runs;
DROP TRIGGER IF EXISTS trg_pulse_ai_072_purged_answer_feedback_guard
    ON pulse_ai_answer_feedback;
DROP FUNCTION IF EXISTS pulse_ai_072_validate_attachment_ownership();
DROP FUNCTION IF EXISTS pulse_ai_072_touch_attachment();
DROP FUNCTION IF EXISTS pulse_ai_072_block_conversation_owner_reassignment();
DROP FUNCTION IF EXISTS pulse_ai_072_guard_chat_document_delete();
DROP FUNCTION IF EXISTS pulse_ai_072_block_purged_answer_resurrection();
DROP FUNCTION IF EXISTS pulse_ai_072_guard_purged_answer_feedback();
DROP TABLE IF EXISTS pulse_ai_conversation_attachments;
DROP TABLE IF EXISTS pulse_ai_conversation_attachment_purge_audit;

ALTER TABLE project_intake_documents
    DROP CONSTRAINT IF EXISTS ck_project_intake_documents_origin_owner;

DO $celar_ai_072_restore_request_required$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM project_intake_documents
        WHERE project_intake_request_id IS NULL
    ) THEN
        RAISE EXCEPTION 'Refusing to restore the intake-request NOT NULL constraint while chat-originated documents remain.';
    END IF;
    ALTER TABLE project_intake_documents
        ALTER COLUMN project_intake_request_id SET NOT NULL;
END;
$celar_ai_072_restore_request_required$;

DELETE FROM app_role_permissions
WHERE app_permission_id = (
    SELECT app_permission_id
    FROM app_permissions
    WHERE permission_code = 'ATTACH_CELAR_AI_CHAT_DOCUMENTS'
);
DELETE FROM app_feature_catalog
WHERE feature_code = 'CELAR_AI_CHAT_ATTACHMENTS';
DELETE FROM app_permissions
WHERE permission_code = 'ATTACH_CELAR_AI_CHAT_DOCUMENTS';
DELETE FROM schema_migrations
WHERE migration_id = '072_celar_ai_conversation_attachments';

COMMIT;
