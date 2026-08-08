-- Pulse migration 081
-- Celar AI private-runtime admission and Work Register filename repair.
--
-- This migration does not claim that OCR, inference, malware scanning, or
-- embeddings are reachable. Deployment supplies and verifies those services.
-- It repairs durable document metadata and creates the least-privilege identity
-- used to admit eligible private documents to the existing processing queue.

BEGIN;

DO $projectpulse081_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.app_users') IS NULL
       OR to_regclass('public.app_roles') IS NULL
       OR to_regclass('public.app_permissions') IS NULL
       OR to_regclass('public.app_role_permissions') IS NULL
       OR to_regclass('public.app_user_role_assignments') IS NULL
       OR to_regclass('public.project_intake_documents') IS NULL
       OR to_regclass('public.work_register_documents') IS NULL THEN
        RAISE EXCEPTION 'Migration 081 requires the identity, RBAC, private-document, and Work Register foundations.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id = '079_coordinated_runtime_ai_document_rbac_repair'
    ) OR NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id = '080_celar_ai_internal_data_intelligence'
    ) THEN
        RAISE EXCEPTION 'Migration 081 requires migrations 079 and 080.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM app_permissions
        WHERE permission_code = 'QUEUE_PULSE_AI_DOCUMENT_PROCESSING'
    ) THEN
        RAISE EXCEPTION 'Migration 081 requires QUEUE_PULSE_AI_DOCUMENT_PROCESSING.';
    END IF;
END;
$projectpulse081_prerequisites$;

CREATE TABLE IF NOT EXISTS module081_private_runtime_records (
    record_type VARCHAR(40) NOT NULL,
    record_id UUID NOT NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (record_type, record_id),
    CONSTRAINT chk_module081_private_runtime_record_type
        CHECK (record_type IN ('service_user','service_role','role_permission','role_assignment'))
);

DO $projectpulse081_service_identity$
DECLARE
    service_user_id CONSTANT UUID := '08100000-0000-0000-0000-000000000001';
    service_role_id CONSTANT UUID := '08100000-0000-0000-0000-000000000002';
    role_permission_id CONSTANT UUID := '08100000-0000-0000-0000-000000000003';
    role_assignment_id CONSTANT UUID := '08100000-0000-0000-0000-000000000004';
    queue_permission_id UUID;
BEGIN
    IF EXISTS (
        SELECT 1 FROM app_users
        WHERE email = 'celar-ai-document-worker@service.projectpulse.internal'
          AND user_id <> service_user_id
    ) THEN
        RAISE EXCEPTION 'The Celar AI document-worker email belongs to a different identity.';
    END IF;
    IF EXISTS (
        SELECT 1 FROM app_users
        WHERE user_id = service_user_id
          AND email <> 'celar-ai-document-worker@service.projectpulse.internal'
    ) THEN
        RAISE EXCEPTION 'The Celar AI document-worker identifier belongs to a different identity.';
    END IF;
    IF EXISTS (
        SELECT 1 FROM app_roles
        WHERE role_code = 'CELAR_AI_DOCUMENT_SERVICE'
          AND app_role_id <> service_role_id
    ) THEN
        RAISE EXCEPTION 'The Celar AI document-service role belongs to a different identity.';
    END IF;
    IF EXISTS (
        SELECT 1 FROM app_roles
        WHERE app_role_id = service_role_id
          AND role_code <> 'CELAR_AI_DOCUMENT_SERVICE'
    ) THEN
        RAISE EXCEPTION 'The Celar AI document-service role identifier belongs to a different role.';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM app_users WHERE user_id = service_user_id) THEN
        INSERT INTO app_users(user_id, entra_object_id, email, display_name, job_title, department, is_active)
        VALUES (
            service_user_id,
            NULL,
            'celar-ai-document-worker@service.projectpulse.internal',
            'Celar AI Private Document Worker',
            'Non-human service identity',
            'Platform Engineering',
            TRUE
        );
        INSERT INTO module081_private_runtime_records(record_type, record_id)
        VALUES ('service_user', service_user_id)
        ON CONFLICT DO NOTHING;
    ELSE
        UPDATE app_users
        SET is_active = TRUE,
            updated_at = NOW()
        WHERE user_id = service_user_id;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM app_roles WHERE app_role_id = service_role_id) THEN
        INSERT INTO app_roles(
            app_role_id, role_code, role_name, role_description,
            is_system_role, is_active, display_order
        ) VALUES (
            service_role_id,
            'CELAR_AI_DOCUMENT_SERVICE',
            'Celar AI Private Document Service',
            'Least-privilege non-human role used only to queue eligible private documents.',
            TRUE,
            TRUE,
            5
        );
        INSERT INTO module081_private_runtime_records(record_type, record_id)
        VALUES ('service_role', service_role_id)
        ON CONFLICT DO NOTHING;
    ELSE
        UPDATE app_roles
        SET is_active = TRUE,
            updated_at = NOW()
        WHERE app_role_id = service_role_id;
    END IF;

    SELECT app_permission_id INTO STRICT queue_permission_id
    FROM app_permissions
    WHERE permission_code = 'QUEUE_PULSE_AI_DOCUMENT_PROCESSING';

    IF NOT EXISTS (
        SELECT 1 FROM app_role_permissions
        WHERE app_role_id = service_role_id
          AND app_permission_id = queue_permission_id
    ) THEN
        INSERT INTO app_role_permissions(
            app_role_permission_id, app_role_id, app_permission_id, created_at
        ) VALUES (
            role_permission_id, service_role_id, queue_permission_id, NOW()
        );
        INSERT INTO module081_private_runtime_records(record_type, record_id)
        VALUES ('role_permission', role_permission_id)
        ON CONFLICT DO NOTHING;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM app_user_role_assignments
        WHERE user_id = service_user_id
          AND app_role_id = service_role_id
    ) THEN
        INSERT INTO app_user_role_assignments(
            app_user_role_assignment_id, user_id, app_role_id,
            assigned_by_user_id, assignment_reason, is_active,
            assigned_at, updated_at
        ) VALUES (
            role_assignment_id,
            service_user_id,
            service_role_id,
            NULL,
            'migration_081_private_document_admission',
            TRUE,
            NOW(),
            NOW()
        );
        INSERT INTO module081_private_runtime_records(record_type, record_id)
        VALUES ('role_assignment', role_assignment_id)
        ON CONFLICT DO NOTHING;
    ELSE
        UPDATE app_user_role_assignments
        SET is_active = TRUE,
            assignment_reason = 'migration_081_private_document_admission',
            updated_at = NOW()
        WHERE user_id = service_user_id
          AND app_role_id = service_role_id;
    END IF;
END;
$projectpulse081_service_identity$;

CREATE OR REPLACE FUNCTION projectpulse081_supported_file_name(
    original_name TEXT,
    stored_path TEXT
)
RETURNS TEXT
LANGUAGE plpgsql
IMMUTABLE
STRICT
AS $projectpulse081_supported_file_name_body$
DECLARE
    clean_name TEXT := btrim(original_name);
    supported_extension TEXT;
BEGIN
    IF lower(clean_name) ~ '\.(pdf|docx|xlsx|pptx|txt|csv|json|xml|html|htm|md)$' THEN
        RETURN clean_name;
    END IF;

    supported_extension := substring(
        lower(regexp_replace(stored_path, '^.*/', ''))
        FROM '(\.(pdf|docx|xlsx|pptx|txt|csv|json|xml|html|htm|md))$'
    );
    IF supported_extension IS NULL OR supported_extension = '' THEN
        RETURN clean_name;
    END IF;
    RETURN clean_name || supported_extension;
END;
$projectpulse081_supported_file_name_body$;

CREATE OR REPLACE FUNCTION projectpulse081_repair_work_register_bridge_name()
RETURNS TRIGGER
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $projectpulse081_repair_work_register_bridge_name_body$
DECLARE
    repaired_name TEXT;
BEGIN
    IF COALESCE(NEW.upload_source, '') <> 'local_file'
       OR COALESCE(NEW.stored_file_path, '') = '' THEN
        RETURN NEW;
    END IF;

    repaired_name := projectpulse081_supported_file_name(
        COALESCE(NULLIF(NEW.original_file_name, ''), NULLIF(NEW.document_name, ''), 'project-document'),
        NEW.stored_file_path
    );

    UPDATE project_intake_documents
    SET original_file_name = repaired_name,
        extraction_status = CASE
            WHEN original_file_name IS DISTINCT FROM repaired_name THEN 'not_started'
            ELSE extraction_status
        END,
        pulse_ai_processing_status = CASE
            WHEN original_file_name IS DISTINCT FROM repaired_name THEN 'not_requested'
            ELSE pulse_ai_processing_status
        END,
        pulse_ai_active_version_id = CASE
            WHEN original_file_name IS DISTINCT FROM repaired_name THEN NULL
            ELSE pulse_ai_active_version_id
        END,
        pulse_ai_processing_error_code = CASE
            WHEN original_file_name IS DISTINCT FROM repaired_name THEN NULL
            ELSE pulse_ai_processing_error_code
        END,
        pulse_ai_processing_updated_at = NOW()
    WHERE work_register_document_id = NEW.work_register_document_id
      AND upload_source = 'work_register_bridge';

    RETURN NEW;
END;
$projectpulse081_repair_work_register_bridge_name_body$;

DROP TRIGGER IF EXISTS trg_projectpulse081_repair_work_register_bridge_name
    ON work_register_documents;
CREATE TRIGGER trg_projectpulse081_repair_work_register_bridge_name
AFTER INSERT OR UPDATE OF document_name, original_file_name, stored_file_path, upload_source
ON work_register_documents
FOR EACH ROW
EXECUTE FUNCTION projectpulse081_repair_work_register_bridge_name();

-- Repair rows imported by migration 079 before this migration existed. The
-- update deliberately returns an unsupported name unchanged when the durable
-- path does not prove a supported file type.
WITH repair AS (
    SELECT
        bridge.project_intake_document_id,
        projectpulse081_supported_file_name(
            bridge.original_file_name,
            source.stored_file_path
        ) AS repaired_name
    FROM project_intake_documents bridge
    JOIN work_register_documents source
      ON source.work_register_document_id = bridge.work_register_document_id
    WHERE bridge.upload_source = 'work_register_bridge'
      AND COALESCE(source.upload_source, '') = 'local_file'
      AND COALESCE(source.stored_file_path, '') <> ''
)
UPDATE project_intake_documents bridge
SET original_file_name = repair.repaired_name,
    extraction_status = 'not_started',
    pulse_ai_processing_status = 'not_requested',
    pulse_ai_active_version_id = NULL,
    pulse_ai_processing_error_code = NULL,
    pulse_ai_processing_updated_at = NOW()
FROM repair
WHERE bridge.project_intake_document_id = repair.project_intake_document_id
  AND bridge.original_file_name IS DISTINCT FROM repair.repaired_name;

DO $projectpulse081_runtime_grants$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'ptp_app') THEN
        EXECUTE 'GRANT SELECT ON TABLE module081_private_runtime_records TO ptp_app';
    END IF;
END;
$projectpulse081_runtime_grants$;

INSERT INTO schema_migrations(migration_id, description, applied_at)
VALUES (
    '081_celar_ai_private_runtime_activation',
    'Create the least-privilege private-document service identity and repair supported Work Register filenames for extraction admission',
    NOW()
)
ON CONFLICT(migration_id) DO NOTHING;

COMMIT;
