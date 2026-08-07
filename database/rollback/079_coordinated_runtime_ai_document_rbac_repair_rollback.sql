-- Guarded rollback for migration 079.
-- Canonical bridge rows must be retired deliberately before their owner column
-- and synchronization trigger can be removed.

BEGIN;

DO $projectpulse_079_rollback_guard$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM project_intake_documents
        WHERE upload_source = 'work_register_bridge'
           OR work_register_document_id IS NOT NULL
    ) THEN
        RAISE EXCEPTION 'Rollback 079 refused: Work Register bridge documents exist. Preserve or retire those canonical document records first.';
    END IF;
END;
$projectpulse_079_rollback_guard$;

DROP TRIGGER IF EXISTS trg_projectpulse079_sync_work_register_document
    ON work_register_documents;
DROP TRIGGER IF EXISTS trg_projectpulse079_archive_deleted_work_register_document
    ON work_register_documents;
DROP FUNCTION IF EXISTS projectpulse079_sync_work_register_document();

DROP INDEX IF EXISTS ux_project_intake_documents_work_register_document;

ALTER TABLE project_intake_documents
    DROP CONSTRAINT IF EXISTS ck_project_intake_documents_origin_owner,
    DROP COLUMN IF EXISTS work_register_document_id;

ALTER TABLE project_intake_documents
    ADD CONSTRAINT ck_project_intake_documents_origin_owner
    CHECK (
        project_intake_request_id IS NOT NULL
        OR (
            COALESCE(upload_source, '') = 'celar_ai_chat_attachment'
            AND uploaded_by_user_id IS NOT NULL
        )
    );

DELETE FROM app_role_permissions grant_row
USING module079_role_grants created
WHERE grant_row.app_role_id = created.app_role_id
  AND grant_row.app_permission_id = created.app_permission_id;

DROP TABLE IF EXISTS module079_role_grants;

DELETE FROM schema_migrations
WHERE migration_id = '079_coordinated_runtime_ai_document_rbac_repair';

COMMIT;
