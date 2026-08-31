-- ProjectPulse migration 096
-- Durable project-document authority shared by Module 055C, FlowHive, and Project Forge.
BEGIN;

CREATE TABLE IF NOT EXISTS project_planning_document_authority (
    authority_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id uuid NOT NULL,
    document_id uuid NOT NULL,
    document_version_id uuid NULL,
    document_role text NOT NULL CHECK (document_role IN (
        'statement_of_work','gsd','design','architecture','requirements','proposal',
        'order','runbook','implementation','validation','change','acceptance','closeout','supporting'
    )),
    source_system text NOT NULL DEFAULT 'module_055c_work_register',
    source_record_id uuid NULL,
    source_file_name text NOT NULL DEFAULT '',
    source_version text NOT NULL DEFAULT '',
    source_sha256 text NOT NULL DEFAULT '',
    processing_status text NOT NULL DEFAULT 'pending',
    index_status text NOT NULL DEFAULT 'pending',
    citation_status text NOT NULL DEFAULT 'pending',
    is_current boolean NOT NULL DEFAULT TRUE,
    authority_metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    established_at timestamptz NOT NULL DEFAULT now(),
    superseded_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CHECK (source_sha256 = '' OR source_sha256 ~ '^[0-9a-fA-F]{64}$'),
    CHECK ((is_current AND superseded_at IS NULL) OR (NOT is_current))
);

CREATE INDEX IF NOT EXISTS ix_project_planning_document_authority_project
    ON project_planning_document_authority(project_id, is_current, document_role);
CREATE INDEX IF NOT EXISTS ix_project_planning_document_authority_document
    ON project_planning_document_authority(document_id, document_version_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_project_planning_document_authority_primary_current
    ON project_planning_document_authority(project_id, document_role)
    WHERE is_current AND document_role IN ('statement_of_work','gsd');
CREATE UNIQUE INDEX IF NOT EXISTS ux_project_planning_document_authority_version_current
    ON project_planning_document_authority(
        project_id,
        document_id,
        COALESCE(document_version_id, '00000000-0000-0000-0000-000000000000'::uuid)
    )
    WHERE is_current;

CREATE OR REPLACE FUNCTION projectpulse_reconcile_project_planning_document_authority(
    p_project_id uuid,
    p_document_id uuid,
    p_document_version_id uuid,
    p_document_role text,
    p_source_system text,
    p_source_record_id uuid,
    p_source_file_name text,
    p_source_version text,
    p_source_sha256 text,
    p_processing_status text,
    p_index_status text,
    p_citation_status text,
    p_metadata jsonb DEFAULT '{}'::jsonb
) RETURNS uuid
LANGUAGE plpgsql
AS $$
DECLARE
    v_authority_id uuid;
    v_role text := lower(trim(COALESCE(p_document_role, 'supporting')));
BEGIN
    IF p_project_id IS NULL OR p_document_id IS NULL THEN
        RAISE EXCEPTION 'project_id and document_id are required';
    END IF;

    IF v_role NOT IN (
        'statement_of_work','gsd','design','architecture','requirements','proposal',
        'order','runbook','implementation','validation','change','acceptance','closeout','supporting'
    ) THEN
        v_role := 'supporting';
    END IF;

    UPDATE project_planning_document_authority
       SET is_current = FALSE,
           superseded_at = now(),
           updated_at = now()
     WHERE project_id = p_project_id
       AND is_current
       AND (
            (v_role IN ('statement_of_work','gsd') AND document_role = v_role)
            OR (
                document_id = p_document_id
                AND COALESCE(document_version_id, '00000000-0000-0000-0000-000000000000'::uuid)
                    <> COALESCE(p_document_version_id, '00000000-0000-0000-0000-000000000000'::uuid)
            )
       );

    SELECT authority_id
      INTO v_authority_id
      FROM project_planning_document_authority
     WHERE project_id = p_project_id
       AND document_id = p_document_id
       AND COALESCE(document_version_id, '00000000-0000-0000-0000-000000000000'::uuid)
           = COALESCE(p_document_version_id, '00000000-0000-0000-0000-000000000000'::uuid)
     ORDER BY created_at DESC
     LIMIT 1;

    IF v_authority_id IS NULL THEN
        INSERT INTO project_planning_document_authority (
            project_id,
            document_id,
            document_version_id,
            document_role,
            source_system,
            source_record_id,
            source_file_name,
            source_version,
            source_sha256,
            processing_status,
            index_status,
            citation_status,
            is_current,
            authority_metadata
        ) VALUES (
            p_project_id,
            p_document_id,
            p_document_version_id,
            v_role,
            COALESCE(NULLIF(trim(p_source_system), ''), 'module_055c_work_register'),
            p_source_record_id,
            COALESCE(p_source_file_name, ''),
            COALESCE(p_source_version, ''),
            lower(COALESCE(p_source_sha256, '')),
            COALESCE(p_processing_status, 'pending'),
            COALESCE(p_index_status, 'pending'),
            COALESCE(p_citation_status, 'pending'),
            TRUE,
            COALESCE(p_metadata, '{}'::jsonb)
        )
        RETURNING authority_id INTO v_authority_id;
    ELSE
        UPDATE project_planning_document_authority
           SET document_role = v_role,
               source_system = COALESCE(NULLIF(trim(p_source_system), ''), source_system),
               source_record_id = p_source_record_id,
               source_file_name = COALESCE(p_source_file_name, ''),
               source_version = COALESCE(p_source_version, ''),
               source_sha256 = lower(COALESCE(p_source_sha256, '')),
               processing_status = COALESCE(p_processing_status, processing_status),
               index_status = COALESCE(p_index_status, index_status),
               citation_status = COALESCE(p_citation_status, citation_status),
               is_current = TRUE,
               superseded_at = NULL,
               authority_metadata = COALESCE(p_metadata, authority_metadata),
               updated_at = now()
         WHERE authority_id = v_authority_id;
    END IF;

    RETURN v_authority_id;
END;
$$;

CREATE OR REPLACE VIEW current_project_planning_document_authority AS
SELECT *
  FROM project_planning_document_authority
 WHERE is_current;

COMMENT ON TABLE project_planning_document_authority IS
    'Current and historical project document authority shared by Module 055C, FlowHive, Project Forge, and private Celar AI citation processing.';

DO $$
BEGIN
    IF to_regclass('public.schema_migrations') IS NOT NULL
       AND EXISTS (
           SELECT 1
             FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'schema_migrations'
              AND column_name = 'migration_id'
       ) THEN
        EXECUTE 'INSERT INTO schema_migrations(migration_id, description) VALUES ($1, $2) ON CONFLICT DO NOTHING'
           USING
               '096_project_planning_document_authority',
               'Durable project-document authority shared by Module 055C, FlowHive, and Project Forge.';
    END IF;
END;
$$;

COMMIT;
