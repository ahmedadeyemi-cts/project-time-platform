-- Coordinated runtime repair for Module 001A role grants and the canonical
-- private-document lifecycle shared by Modules 019, 001, 033, 055C/055D, and 066.
-- This migration does not attest or enable private infrastructure. It makes
-- uploaded Work Register files discoverable and queueable by the existing
-- private scanning/extraction/embedding pipeline.

BEGIN;

DO $projectpulse_079_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.project_intake_documents') IS NULL
       OR to_regclass('public.work_register_documents') IS NULL THEN
        RAISE EXCEPTION 'Migration 079 requires schema_migrations, project_intake_documents, and work_register_documents.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM (VALUES
            ('052_pulse_ai_private_document_runtime'),
            ('057_module_001_multi_timer_document_grounded_ai'),
            ('072_celar_ai_conversation_attachments'),
            ('078_module_001a_engineer_request_closeout')
        ) required(migration_id)
        WHERE NOT EXISTS (
            SELECT 1 FROM schema_migrations applied
            WHERE applied.migration_id = required.migration_id
        )
    ) THEN
        RAISE EXCEPTION 'Migration 079 requires migrations 052, 057, 072, and 078.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM (VALUES
            ('project_id'), ('document_name'), ('document_type'), ('version_label'),
            ('status'), ('visibility'), ('effective_date'), ('upload_source'),
            ('original_file_name'), ('stored_file_path'), ('content_type'),
            ('file_size_bytes'), ('created_by_user_id'), ('created_at')
        ) required(column_name)
        WHERE NOT EXISTS (
            SELECT 1
            FROM information_schema.columns available
            WHERE available.table_schema = 'public'
              AND available.table_name = 'work_register_documents'
              AND available.column_name = required.column_name
        )
    ) THEN
        RAISE EXCEPTION 'Migration 079 requires the durable local-upload columns on work_register_documents.';
    END IF;
END;
$projectpulse_079_prerequisites$;

ALTER TABLE project_intake_documents
    ALTER COLUMN project_intake_request_id DROP NOT NULL,
    ADD COLUMN IF NOT EXISTS work_register_document_id UUID NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_project_intake_documents_work_register_document
    ON project_intake_documents(work_register_document_id)
    WHERE work_register_document_id IS NOT NULL;

ALTER TABLE project_intake_documents
    DROP CONSTRAINT IF EXISTS ck_project_intake_documents_origin_owner;

ALTER TABLE project_intake_documents
    ADD CONSTRAINT ck_project_intake_documents_origin_owner
    CHECK (
        project_intake_request_id IS NOT NULL
        OR (
            COALESCE(upload_source, '') = 'celar_ai_chat_attachment'
            AND uploaded_by_user_id IS NOT NULL
        )
        OR (
            COALESCE(upload_source, '') = 'work_register_bridge'
            AND project_id IS NOT NULL
            AND work_register_document_id IS NOT NULL
        )
    );

CREATE OR REPLACE FUNCTION projectpulse079_sync_work_register_document()
RETURNS TRIGGER
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $projectpulse079_sync_work_register_document_body$
DECLARE
    source_row work_register_documents%ROWTYPE;
    category_code TEXT;
    engineering_visible_value BOOLEAN;
BEGIN
    IF TG_OP = 'DELETE' THEN
        UPDATE project_intake_documents
        SET is_active = FALSE,
            document_status = 'archived',
            pulse_ai_processing_updated_at = NOW()
        WHERE work_register_document_id = OLD.work_register_document_id;
        RETURN OLD;
    END IF;

    source_row := NEW;

    -- Link-only records remain visible in Work Register, but only durable local
    -- files can enter the private extraction and retrieval lifecycle.
    IF COALESCE(source_row.upload_source, '') <> 'local_file'
       OR COALESCE(source_row.stored_file_path, '') = '' THEN
        UPDATE project_intake_documents
        SET is_active = FALSE,
            document_status = 'archived',
            pulse_ai_processing_updated_at = NOW()
        WHERE work_register_document_id = source_row.work_register_document_id;
        RETURN NEW;
    END IF;

    category_code := CASE
        WHEN lower(COALESCE(source_row.document_type, '')) IN ('sow', 'statement of work', 'statement_of_work') THEN 'sow'
        WHEN lower(COALESCE(source_row.document_type, '')) IN ('gsd', 'global solution design', 'global_solution_design') THEN 'gsd'
        WHEN lower(COALESCE(source_row.document_type, '')) IN ('technical document', 'architecture', 'design') THEN 'architecture'
        WHEN lower(COALESCE(source_row.document_type, '')) IN ('project plan', 'project_plan') THEN 'project_plan'
        WHEN lower(COALESCE(source_row.document_type, '')) IN ('change order', 'change_order') THEN 'change_order'
        WHEN lower(COALESCE(source_row.document_type, '')) IN ('customer approval', 'customer_approval') THEN 'customer_approval'
        WHEN lower(COALESCE(source_row.document_type, '')) = 'closeout' THEN 'closeout'
        ELSE 'supporting'
    END;
    engineering_visible_value := lower(COALESCE(source_row.visibility, 'project_team'))
        IN ('project_team', 'engineering_team', 'all');

    INSERT INTO project_intake_documents (
        project_intake_document_id,
        project_intake_request_id,
        project_id,
        work_register_document_id,
        document_type,
        document_category,
        document_status,
        original_file_name,
        stored_file_name,
        storage_path,
        content_type,
        size_bytes,
        uploaded_by_user_id,
        upload_source,
        extraction_status,
        engineering_visible,
        ai_timesheet_context_enabled,
        source_system,
        external_reference_id,
        pulse_ai_document_revision,
        pulse_ai_effective_at,
        pulse_ai_processing_status,
        uploaded_at,
        is_active
    ) VALUES (
        gen_random_uuid(),
        NULL,
        source_row.project_id,
        source_row.work_register_document_id,
        lower(regexp_replace(COALESCE(NULLIF(source_row.document_type, ''), 'supporting'), '[^a-zA-Z0-9]+', '_', 'g')),
        category_code,
        CASE WHEN lower(COALESCE(source_row.status, 'active')) = 'active' THEN 'active' ELSE 'archived' END,
        COALESCE(NULLIF(source_row.original_file_name, ''), NULLIF(source_row.document_name, ''), 'project-document'),
        regexp_replace(source_row.stored_file_path, '^.*/', ''),
        source_row.stored_file_path,
        NULLIF(source_row.content_type, ''),
        GREATEST(COALESCE(source_row.file_size_bytes, 0), 0),
        source_row.created_by_user_id,
        'work_register_bridge',
        'not_started',
        engineering_visible_value,
        engineering_visible_value,
        'work_register_055c_055d',
        source_row.work_register_document_id::TEXT,
        COALESCE(source_row.version_label, ''),
        source_row.effective_date::TIMESTAMPTZ,
        'not_requested',
        COALESCE(source_row.created_at, NOW()),
        lower(COALESCE(source_row.status, 'active')) = 'active'
    )
    ON CONFLICT (work_register_document_id) WHERE work_register_document_id IS NOT NULL
    DO UPDATE SET
        project_id = EXCLUDED.project_id,
        document_type = EXCLUDED.document_type,
        document_category = EXCLUDED.document_category,
        document_status = EXCLUDED.document_status,
        original_file_name = EXCLUDED.original_file_name,
        stored_file_name = EXCLUDED.stored_file_name,
        storage_path = EXCLUDED.storage_path,
        content_type = EXCLUDED.content_type,
        size_bytes = EXCLUDED.size_bytes,
        engineering_visible = EXCLUDED.engineering_visible,
        ai_timesheet_context_enabled = EXCLUDED.ai_timesheet_context_enabled,
        pulse_ai_document_revision = EXCLUDED.pulse_ai_document_revision,
        pulse_ai_effective_at = EXCLUDED.pulse_ai_effective_at,
        extraction_status = CASE
            WHEN project_intake_documents.storage_path IS DISTINCT FROM EXCLUDED.storage_path
              OR project_intake_documents.size_bytes IS DISTINCT FROM EXCLUDED.size_bytes
              OR project_intake_documents.pulse_ai_document_revision IS DISTINCT FROM EXCLUDED.pulse_ai_document_revision
            THEN 'not_started'
            ELSE project_intake_documents.extraction_status
        END,
        pulse_ai_processing_status = CASE
            WHEN project_intake_documents.storage_path IS DISTINCT FROM EXCLUDED.storage_path
              OR project_intake_documents.size_bytes IS DISTINCT FROM EXCLUDED.size_bytes
              OR project_intake_documents.pulse_ai_document_revision IS DISTINCT FROM EXCLUDED.pulse_ai_document_revision
            THEN 'not_requested'
            ELSE project_intake_documents.pulse_ai_processing_status
        END,
        pulse_ai_active_version_id = CASE
            WHEN project_intake_documents.storage_path IS DISTINCT FROM EXCLUDED.storage_path
              OR project_intake_documents.size_bytes IS DISTINCT FROM EXCLUDED.size_bytes
              OR project_intake_documents.pulse_ai_document_revision IS DISTINCT FROM EXCLUDED.pulse_ai_document_revision
            THEN NULL
            ELSE project_intake_documents.pulse_ai_active_version_id
        END,
        pulse_ai_processing_updated_at = NOW(),
        is_active = EXCLUDED.is_active;

    RETURN NEW;
END;
$projectpulse079_sync_work_register_document_body$;

DROP TRIGGER IF EXISTS trg_projectpulse079_sync_work_register_document
    ON work_register_documents;
CREATE TRIGGER trg_projectpulse079_sync_work_register_document
AFTER INSERT OR UPDATE OF project_id, document_name, document_type, version_label,
    status, visibility, effective_date, upload_source, original_file_name,
    stored_file_path, content_type, file_size_bytes
ON work_register_documents
FOR EACH ROW
EXECUTE FUNCTION projectpulse079_sync_work_register_document();

DROP TRIGGER IF EXISTS trg_projectpulse079_archive_deleted_work_register_document
    ON work_register_documents;
CREATE TRIGGER trg_projectpulse079_archive_deleted_work_register_document
AFTER DELETE ON work_register_documents
FOR EACH ROW
EXECUTE FUNCTION projectpulse079_sync_work_register_document();

-- Backfill all durable local uploads by touching a governed trigger column.
-- The existing project_intake_documents triggers will then queue eligible,
-- engineering-visible rows for the private pipeline when runtime policy allows.
UPDATE work_register_documents
SET status = status
WHERE COALESCE(upload_source, '') = 'local_file'
  AND COALESCE(stored_file_path, '') <> '';

CREATE TABLE IF NOT EXISTS module079_role_grants (
    app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE RESTRICT,
    app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (app_role_id, app_permission_id)
);

WITH desired(role_code, permission_code) AS (
    VALUES
        ('ENGINEERING_LEAD', 'VIEW_ENGINEER_TASK_CLOSEOUT_001A'),
        ('ENGINEERING_LEAD', 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A'),
        ('ENGINEERING_TEAM_LEAD', 'VIEW_ENGINEER_TASK_CLOSEOUT_001A'),
        ('ENGINEERING_TEAM_LEAD', 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A'),
        ('ENGINEERING_MANAGER', 'VIEW_ENGINEER_TASK_CLOSEOUT_001A'),
        ('ENGINEERING_MANAGER', 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A')
), candidates AS (
    SELECT role.app_role_id, permission.app_permission_id
    FROM desired
    JOIN app_roles role
  ON UPPER(role.role_code) = desired.role_code
 AND role.is_active = TRUE
    JOIN app_permissions permission
  ON permission.permission_code = desired.permission_code
    LEFT JOIN app_role_permissions existing
  ON existing.app_role_id = role.app_role_id
 AND existing.app_permission_id = permission.app_permission_id
    WHERE existing.app_role_permission_id IS NULL
), inserted AS (
    INSERT INTO app_role_permissions(app_role_id, app_permission_id, created_at)
    SELECT app_role_id, app_permission_id, NOW()
    FROM candidates
    ON CONFLICT(app_role_id, app_permission_id) DO NOTHING
    RETURNING app_role_id, app_permission_id
)
INSERT INTO module079_role_grants(app_role_id, app_permission_id)
SELECT app_role_id, app_permission_id FROM inserted
ON CONFLICT DO NOTHING;

INSERT INTO schema_migrations(migration_id, description, applied_at)
VALUES(
    '079_coordinated_runtime_ai_document_rbac_repair',
    'Bridge Work Register uploads into the canonical private-document lifecycle and grant Module 001A access to Engineering Leads and Managers',
    NOW())
ON CONFLICT(migration_id) DO NOTHING;

COMMIT;
