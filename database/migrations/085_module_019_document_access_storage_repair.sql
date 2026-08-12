-- Module 019 assigned-work document access and canonical upload-path repair.
--
-- This migration is intentionally environment-neutral and idempotent. The
-- protected Test release process is responsible for applying it first in Test.
-- It does not move, delete, or fabricate document bytes.

BEGIN;

DO $module019_085_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.project_intake_documents') IS NULL
       OR to_regclass('public.work_register_documents') IS NULL
       OR to_regclass('public.project_assignments') IS NULL
       OR to_regclass('public.engineering_resource_requests') IS NULL
       OR to_regclass('public.engineering_resource_request_assignments') IS NULL THEN
        RAISE EXCEPTION 'Migration 085 requires the Module 019, 055C, 055D, and assignment tables.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM schema_migrations
        WHERE migration_id = '079_coordinated_runtime_ai_document_rbac_repair'
    ) THEN
        RAISE EXCEPTION 'Migration 085 requires migration 079.';
    END IF;
END;
$module019_085_prerequisites$;

CREATE OR REPLACE FUNCTION projectpulse085_normalize_upload_path(input_path TEXT)
RETURNS TEXT
LANGUAGE plpgsql
IMMUTABLE
STRICT
SET search_path = pg_catalog
AS $projectpulse085_normalize_upload_path_body$
DECLARE
    normalized TEXT;
BEGIN
    normalized := replace(btrim(input_path), E'\\', '/');

    IF normalized = ''
       OR normalized ~ '(^|/)\.\.(/|$)'
       OR normalized ~ '(^|/)\.(/|$)'
       OR normalized ~* '^(file|https?):' THEN
        RETURN input_path;
    END IF;

    normalized := regexp_replace(normalized, '^\./+', '');

    -- Canonicalize absolute application paths only when they contain the
    -- governed upload-volume boundary. Unknown absolute paths remain unchanged
    -- so they can be reported and reconciled instead of silently redirected.
    IF normalized ~ '^/' OR normalized ~ '^[A-Za-z]:/' THEN
        IF normalized ~* '/uploads/' THEN
            normalized := regexp_replace(normalized, '^.*/uploads/', '', 'i');
        ELSE
            RETURN input_path;
        END IF;
    ELSIF normalized ~* '^uploads/' THEN
        normalized := regexp_replace(normalized, '^uploads/', '', 'i');
    END IF;

    normalized := regexp_replace(normalized, '^/+', '');
    IF normalized = ''
       OR normalized ~ '(^|/)\.\.(/|$)'
       OR normalized ~ '(^|/)\.(/|$)' THEN
        RETURN input_path;
    END IF;

    RETURN normalized;
END;
$projectpulse085_normalize_upload_path_body$;

CREATE OR REPLACE FUNCTION projectpulse085_normalize_work_register_upload_path()
RETURNS TRIGGER
LANGUAGE plpgsql
SET search_path = public, pg_temp
AS $projectpulse085_normalize_work_register_upload_path_body$
BEGIN
    IF NEW.stored_file_path IS NOT NULL AND btrim(NEW.stored_file_path) <> '' THEN
        NEW.stored_file_path := projectpulse085_normalize_upload_path(NEW.stored_file_path);
    END IF;
    RETURN NEW;
END;
$projectpulse085_normalize_work_register_upload_path_body$;

DROP TRIGGER IF EXISTS trg_projectpulse085_normalize_work_register_upload_path
    ON work_register_documents;
CREATE TRIGGER trg_projectpulse085_normalize_work_register_upload_path
BEFORE INSERT OR UPDATE OF stored_file_path
ON work_register_documents
FOR EACH ROW
EXECUTE FUNCTION projectpulse085_normalize_work_register_upload_path();

CREATE OR REPLACE FUNCTION projectpulse085_normalize_intake_upload_path()
RETURNS TRIGGER
LANGUAGE plpgsql
SET search_path = public, pg_temp
AS $projectpulse085_normalize_intake_upload_path_body$
BEGIN
    IF NEW.storage_path IS NOT NULL AND btrim(NEW.storage_path) <> '' THEN
        NEW.storage_path := projectpulse085_normalize_upload_path(NEW.storage_path);
    END IF;
    RETURN NEW;
END;
$projectpulse085_normalize_intake_upload_path_body$;

DROP TRIGGER IF EXISTS trg_projectpulse085_normalize_intake_upload_path
    ON project_intake_documents;
CREATE TRIGGER trg_projectpulse085_normalize_intake_upload_path
BEFORE INSERT OR UPDATE OF storage_path
ON project_intake_documents
FOR EACH ROW
EXECUTE FUNCTION projectpulse085_normalize_intake_upload_path();

-- Build supporting indexes before canonicalizing legacy rows. Existing Test
-- data can produce deferred constraint or bridge trigger events on
-- project_intake_documents. PostgreSQL refuses CREATE INDEX while those events
-- remain pending in the same transaction.
CREATE INDEX IF NOT EXISTS ix_piw085_project_assignment_user_dates
    ON project_assignments(project_id, user_id, effective_start_date, effective_end_date);

CREATE INDEX IF NOT EXISTS ix_piw085_intake_document_project_active
    ON project_intake_documents(project_id, uploaded_at DESC)
    WHERE is_active = TRUE;

CREATE INDEX IF NOT EXISTS ix_piw085_intake_document_request_active
    ON project_intake_documents(project_intake_request_id, uploaded_at DESC)
    WHERE is_active = TRUE;

CREATE INDEX IF NOT EXISTS ix_piw085_resource_request_project_intake
    ON engineering_resource_requests(project_id, project_intake_request_id, request_status);

CREATE INDEX IF NOT EXISTS ix_piw085_resource_assignment_user_request
    ON engineering_resource_request_assignments(user_id, engineering_resource_request_id);

-- Touch only rows whose approved path can be normalized. Updating the Work
-- Register source first lets the existing migration-079 bridge receive the
-- canonical relative path through its governed AFTER trigger.
UPDATE work_register_documents
SET stored_file_path = projectpulse085_normalize_upload_path(stored_file_path)
WHERE COALESCE(stored_file_path, '') <> ''
  AND stored_file_path IS DISTINCT FROM projectpulse085_normalize_upload_path(stored_file_path);

UPDATE project_intake_documents
SET storage_path = projectpulse085_normalize_upload_path(storage_path)
WHERE COALESCE(storage_path, '') <> ''
  AND storage_path IS DISTINCT FROM projectpulse085_normalize_upload_path(storage_path);

INSERT INTO schema_migrations(migration_id, description, applied_at)
VALUES (
    '085_module_019_document_access_storage_repair',
    'Canonicalize governed 055C/055D upload paths and support assignment-scoped Module 019 document delivery',
    NOW()
)
ON CONFLICT(migration_id) DO NOTHING;

COMMIT;
