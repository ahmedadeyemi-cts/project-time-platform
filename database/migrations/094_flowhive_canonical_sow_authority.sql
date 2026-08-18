-- Pulse migration 094
-- FlowHive canonical SOW authority reconciliation.
--
-- Modules 055C and 019 already prove that an active Work Register SOW is
-- securely stored and downloadable. FlowHive additionally requires the
-- sanitized private version to be authoritative and citation indexed. This
-- migration promotes only the active private version of an active, local-file,
-- Work Register SOW after private processing is ready. It does not read or copy
-- document text, bypass malware scanning, create citations, fabricate scope,
-- approve an ad-hoc upload, or send any content to an external provider.

BEGIN;

DO $projectpulse094_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.project_intake_documents') IS NULL
       OR to_regclass('public.work_register_documents') IS NULL
       OR to_regclass('public.pulse_ai_document_versions') IS NULL THEN
        RAISE EXCEPTION 'Migration 094 requires schema_migrations, Work Register document bridging, and the private document-version runtime.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM schema_migrations
        WHERE migration_id = '079_coordinated_runtime_ai_document_rbac_repair'
    ) OR NOT EXISTS (
        SELECT 1
        FROM schema_migrations
        WHERE migration_id = '081_celar_ai_private_runtime_activation'
    ) OR NOT EXISTS (
        SELECT 1
        FROM schema_migrations
        WHERE migration_id = '086_module_066_flowhive_enterprise_pm'
    ) THEN
        RAISE EXCEPTION 'Migration 094 requires migrations 079, 081, and 086.';
    END IF;
END;
$projectpulse094_prerequisites$;

CREATE TABLE IF NOT EXISTS module094_flowhive_sow_authority_evidence (
    pulse_ai_document_version_id UUID PRIMARY KEY,
    project_intake_document_id UUID NOT NULL,
    work_register_document_id UUID NOT NULL,
    project_id UUID NOT NULL,
    previous_authority_status VARCHAR(40) NOT NULL,
    promoted_authority_status VARCHAR(40) NOT NULL,
    document_version VARCHAR(300) NOT NULL,
    source_sha256 VARCHAR(64) NOT NULL,
    promotion_reason VARCHAR(160) NOT NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_module094_previous_authority
        CHECK (previous_authority_status IN ('candidate','approved')),
    CONSTRAINT chk_module094_promoted_authority
        CHECK (promoted_authority_status IN ('approved','canonical'))
);

CREATE INDEX IF NOT EXISTS ix_module094_flowhive_sow_authority_project
    ON module094_flowhive_sow_authority_evidence(project_id, recorded_at DESC);

CREATE OR REPLACE FUNCTION projectpulse094_reconcile_ready_work_register_sow(
    target_document_id UUID
)
RETURNS VOID
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $projectpulse094_reconcile_ready_work_register_sow_body$
DECLARE
    document_row RECORD;
    desired_authority VARCHAR(40);
BEGIN
    SELECT
        document.project_intake_document_id,
        document.project_id,
        document.work_register_document_id,
        document.pulse_ai_active_version_id,
        version.authority_status,
        version.document_version,
        version.source_sha256,
        version.index_status
    INTO document_row
    FROM project_intake_documents document
    JOIN work_register_documents source
      ON source.work_register_document_id = document.work_register_document_id
    JOIN pulse_ai_document_versions version
      ON version.pulse_ai_document_version_id = document.pulse_ai_active_version_id
     AND version.project_intake_document_id = document.project_intake_document_id
    WHERE document.project_intake_document_id = target_document_id
      AND document.project_id IS NOT NULL
      AND document.work_register_document_id IS NOT NULL
      AND document.upload_source = 'work_register_bridge'
      AND document.is_active = TRUE
      AND COALESCE(document.engineering_visible, FALSE) = TRUE
      AND LOWER(COALESCE(document.document_category, document.document_type, '')) IN ('sow','statement_of_work')
      AND document.pulse_ai_processing_status = 'ready'
      AND COALESCE(source.upload_source, '') = 'local_file'
      AND COALESCE(source.stored_file_path, '') <> ''
      AND LOWER(COALESCE(source.status, 'active')) = 'active'
      AND LOWER(COALESCE(source.document_type, '')) IN ('sow','statement of work','statement_of_work')
      AND version.authority_status IN ('candidate','approved','canonical')
      AND version.index_status IN ('lexical_ready','embedding_ready','ready')
    FOR UPDATE OF version;

    IF NOT FOUND THEN
        RETURN;
    END IF;
    IF document_row.authority_status = 'canonical' THEN
        RETURN;
    END IF;

    -- Serialize authority selection by project and version label so two workers
    -- completing equivalent SOW records at the same time cannot race the unique
    -- canonical-version boundary.
    PERFORM pg_advisory_xact_lock(
        hashtextextended(
            document_row.project_id::TEXT || '|' || document_row.document_version,
            94
        )
    );

    -- The existing private-runtime index permits one canonical row for a given
    -- project and document_version. A duplicate active source therefore becomes
    -- approved rather than violating the unique authority boundary. FlowHive
    -- accepts either status but continues to show the exact version and citation
    -- evidence used for planning.
    desired_authority := CASE
        WHEN EXISTS (
            SELECT 1
            FROM pulse_ai_document_versions existing
            WHERE existing.project_id = document_row.project_id
              AND existing.document_version = document_row.document_version
              AND existing.authority_status = 'canonical'
              AND existing.pulse_ai_document_version_id <> document_row.pulse_ai_active_version_id
        ) THEN 'approved'
        ELSE 'canonical'
    END;

    IF document_row.authority_status = desired_authority THEN
        RETURN;
    END IF;

    INSERT INTO module094_flowhive_sow_authority_evidence (
        pulse_ai_document_version_id,
        project_intake_document_id,
        work_register_document_id,
        project_id,
        previous_authority_status,
        promoted_authority_status,
        document_version,
        source_sha256,
        promotion_reason
    ) VALUES (
        document_row.pulse_ai_active_version_id,
        document_row.project_intake_document_id,
        document_row.work_register_document_id,
        document_row.project_id,
        document_row.authority_status,
        desired_authority,
        document_row.document_version,
        document_row.source_sha256,
        CASE
            WHEN desired_authority = 'canonical'
                THEN 'active_work_register_sow_private_version_ready'
            ELSE 'active_work_register_sow_duplicate_version_approved'
        END
    )
    ON CONFLICT (pulse_ai_document_version_id) DO NOTHING;

    UPDATE pulse_ai_document_versions
    SET authority_status = desired_authority
    WHERE pulse_ai_document_version_id = document_row.pulse_ai_active_version_id
      AND project_intake_document_id = document_row.project_intake_document_id
      AND authority_status = document_row.authority_status
      AND authority_status IN ('candidate','approved');
END;
$projectpulse094_reconcile_ready_work_register_sow_body$;

CREATE OR REPLACE FUNCTION projectpulse094_reconcile_ready_work_register_sow_trigger()
RETURNS TRIGGER
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $projectpulse094_reconcile_ready_work_register_sow_trigger_body$
BEGIN
    PERFORM projectpulse094_reconcile_ready_work_register_sow(
        NEW.project_intake_document_id
    );
    RETURN NEW;
END;
$projectpulse094_reconcile_ready_work_register_sow_trigger_body$;

DROP TRIGGER IF EXISTS trg_projectpulse094_reconcile_ready_work_register_sow
    ON project_intake_documents;
CREATE TRIGGER trg_projectpulse094_reconcile_ready_work_register_sow
AFTER INSERT OR UPDATE OF
    project_id,
    work_register_document_id,
    upload_source,
    document_type,
    document_category,
    engineering_visible,
    pulse_ai_processing_status,
    pulse_ai_active_version_id,
    is_active
ON project_intake_documents
FOR EACH ROW
EXECUTE FUNCTION projectpulse094_reconcile_ready_work_register_sow_trigger();

-- Reconcile private versions that completed before Migration 094 was applied.
DO $projectpulse094_backfill_ready_sources$
DECLARE
    candidate RECORD;
BEGIN
    FOR candidate IN
        SELECT document.project_intake_document_id
        FROM project_intake_documents document
        JOIN work_register_documents source
          ON source.work_register_document_id = document.work_register_document_id
        WHERE document.project_id IS NOT NULL
          AND document.work_register_document_id IS NOT NULL
          AND document.upload_source = 'work_register_bridge'
          AND document.is_active = TRUE
          AND COALESCE(document.engineering_visible, FALSE) = TRUE
          AND LOWER(COALESCE(document.document_category, document.document_type, '')) IN ('sow','statement_of_work')
          AND document.pulse_ai_processing_status = 'ready'
          AND document.pulse_ai_active_version_id IS NOT NULL
          AND COALESCE(source.upload_source, '') = 'local_file'
          AND COALESCE(source.stored_file_path, '') <> ''
          AND LOWER(COALESCE(source.status, 'active')) = 'active'
        ORDER BY
            COALESCE(document.pulse_ai_effective_at, document.uploaded_at) DESC,
            document.project_intake_document_id
    LOOP
        PERFORM projectpulse094_reconcile_ready_work_register_sow(
            candidate.project_intake_document_id
        );
    END LOOP;
END;
$projectpulse094_backfill_ready_sources$;

DO $projectpulse094_verify$
DECLARE
    invalid_count BIGINT;
BEGIN
    SELECT COUNT(*)
    INTO invalid_count
    FROM module094_flowhive_sow_authority_evidence evidence
    JOIN pulse_ai_document_versions version
      ON version.pulse_ai_document_version_id = evidence.pulse_ai_document_version_id
    WHERE version.authority_status <> evidence.promoted_authority_status;

    IF invalid_count > 0 THEN
        RAISE EXCEPTION 'Migration 094 authority reconciliation did not retain the recorded promoted status for % version(s).', invalid_count;
    END IF;
END;
$projectpulse094_verify$;

INSERT INTO schema_migrations(migration_id, description, applied_at)
VALUES (
    '094_flowhive_canonical_sow_authority',
    'Promote ready private versions of active local Work Register SOWs to canonical or approved FlowHive planning authority with durable evidence',
    NOW()
)
ON CONFLICT(migration_id) DO UPDATE
SET description = EXCLUDED.description;

COMMIT;
