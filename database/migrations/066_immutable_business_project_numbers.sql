-- ProjectPulse migration 066
-- Issue immutable, human-readable business project numbers for Module 055D
-- Work Register projects while preserving UUID identity and every prior WR-* alias.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

DO $projectpulse066_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL THEN
        RAISE EXCEPTION 'Migration 066 requires public.schema_migrations.';
    END IF;
    IF to_regclass('public.projects') IS NULL THEN
        RAISE EXCEPTION 'Migration 066 requires public.projects.';
    END IF;
    IF to_regclass('public.work_register_project_metadata') IS NULL THEN
        RAISE EXCEPTION 'Migration 066 requires public.work_register_project_metadata.';
    END IF;
END;
$projectpulse066_prerequisites$;

ALTER TABLE projects
    ADD COLUMN IF NOT EXISTS legacy_project_code VARCHAR(100),
    ADD COLUMN IF NOT EXISTS project_number_issued_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS project_number_source VARCHAR(80);

ALTER TABLE work_register_project_metadata
    ADD COLUMN IF NOT EXISTS business_project_number VARCHAR(100),
    ADD COLUMN IF NOT EXISTS legacy_project_code VARCHAR(100);

CREATE TABLE IF NOT EXISTS project_business_identifier_aliases (
    project_business_identifier_alias_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    alias_code VARCHAR(100) NOT NULL,
    alias_type VARCHAR(40) NOT NULL,
    is_current BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_project_business_identifier_alias UNIQUE (alias_code),
    CONSTRAINT chk_project_business_identifier_alias_type
        CHECK (alias_type IN ('business_number', 'legacy_work_register'))
);

CREATE INDEX IF NOT EXISTS ix_project_business_identifier_alias_project
    ON project_business_identifier_aliases(project_id, is_current DESC, alias_type);
CREATE UNIQUE INDEX IF NOT EXISTS uq_projects_legacy_project_code
    ON projects(legacy_project_code)
    WHERE legacy_project_code IS NOT NULL AND btrim(legacy_project_code) <> '';

CREATE OR REPLACE FUNCTION projectpulse066_canonical_work_type(value TEXT)
RETURNS TEXT
LANGUAGE SQL
IMMUTABLE
AS $projectpulse066_canonical$
    SELECT CASE regexp_replace(lower(COALESCE(value, '')), '[^a-z0-9]+', '', 'g')
        WHEN 'servicerequest' THEN 'SERVICE_REQUEST'
        WHEN 'sr' THEN 'SERVICE_REQUEST'
        WHEN 'iqs' THEN 'IQS'
        WHEN 'internalproject' THEN 'INTERNAL_PROJECT'
        WHEN 'internal' THEN 'INTERNAL_PROJECT'
        WHEN 'presales' THEN 'PRE_SALES'
        WHEN 'presale' THEN 'PRE_SALES'
        ELSE 'PROJECT'
    END;
$projectpulse066_canonical$;

CREATE OR REPLACE FUNCTION projectpulse066_project_number_prefix(value TEXT)
RETURNS TEXT
LANGUAGE SQL
IMMUTABLE
AS $projectpulse066_prefix$
    SELECT CASE projectpulse066_canonical_work_type(value)
        WHEN 'SERVICE_REQUEST' THEN 'SR'
        WHEN 'IQS' THEN 'IQS'
        WHEN 'INTERNAL_PROJECT' THEN 'INT'
        WHEN 'PRE_SALES' THEN 'PRES'
        ELSE 'PRO'
    END;
$projectpulse066_prefix$;

CREATE OR REPLACE FUNCTION projectpulse066_work_type_from_description(value TEXT)
RETURNS TEXT
LANGUAGE plpgsql
IMMUTABLE
AS $projectpulse066_description$
DECLARE
    normalized TEXT := lower(COALESCE(value, ''));
BEGIN
    IF normalized LIKE '%work type:%service request%' THEN RETURN 'SERVICE_REQUEST'; END IF;
    IF normalized LIKE '%work type:%iqs%' THEN RETURN 'IQS'; END IF;
    IF normalized LIKE '%work type:%internal project%' THEN RETURN 'INTERNAL_PROJECT'; END IF;
    IF normalized LIKE '%work type:%pre-sales%'
       OR normalized LIKE '%work type:%pre sales%'
       OR normalized LIKE '%work type:%presales%' THEN RETURN 'PRE_SALES'; END IF;
    RETURN 'PROJECT';
END;
$projectpulse066_description$;

CREATE OR REPLACE FUNCTION projectpulse066_issue_business_project_number(
    p_work_type TEXT,
    p_project_id UUID)
RETURNS TEXT
LANGUAGE plpgsql
AS $projectpulse066_issue$
DECLARE
    prefix TEXT := projectpulse066_project_number_prefix(p_work_type);
    candidate TEXT;
    attempt INTEGER;
BEGIN
    IF p_project_id IS NULL THEN
        RAISE EXCEPTION 'A project UUID is required before issuing a business project number.';
    END IF;

    FOR attempt IN 0..999 LOOP
        candidate := prefix || '-' || upper(substr(md5(p_project_id::text || ':' || attempt::text), 1, 8));
        IF NOT EXISTS (
            SELECT 1
            FROM projects project
            WHERE upper(project.project_code) = upper(candidate)
              AND project.project_id <> p_project_id
        ) THEN
            RETURN candidate;
        END IF;
    END LOOP;

    RAISE EXCEPTION 'Unable to issue a unique business project number for project %.', p_project_id;
END;
$projectpulse066_issue$;

CREATE OR REPLACE FUNCTION projectpulse066_assign_business_project_number()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse066_assign$
DECLARE
    work_type TEXT;
BEGIN
    IF NEW.project_id IS NULL THEN NEW.project_id := gen_random_uuid(); END IF;

    IF COALESCE(NEW.project_code, '') ~* '^WR-'
       AND lower(COALESCE(NEW.project_description, '')) LIKE '%created from work register intake%' THEN
        NEW.legacy_project_code := COALESCE(NULLIF(NEW.legacy_project_code, ''), NEW.project_code);
        work_type := projectpulse066_work_type_from_description(NEW.project_description);
        NEW.project_code := projectpulse066_issue_business_project_number(work_type, NEW.project_id);
        NEW.project_number_issued_at := COALESCE(NEW.project_number_issued_at, NOW());
        NEW.project_number_source := 'module_055d_work_register';
    END IF;

    RETURN NEW;
END;
$projectpulse066_assign$;

DROP TRIGGER IF EXISTS trg_projects_066_assign_business_number ON projects;
CREATE TRIGGER trg_projects_066_assign_business_number
BEFORE INSERT ON projects
FOR EACH ROW EXECUTE FUNCTION projectpulse066_assign_business_project_number();

-- Convert only Work Register projects. Other established project-code systems
-- remain unchanged and continue using the UUID as the relational identity.
WITH candidates AS (
    SELECT
        project.project_id,
        project.project_code AS legacy_code,
        metadata.requested_work_type
    FROM projects project
    JOIN work_register_project_metadata metadata
      ON metadata.project_id = project.project_id
    WHERE project.project_code ~* '^WR-'
), issued AS (
    SELECT
        candidate.project_id,
        candidate.legacy_code,
        projectpulse066_issue_business_project_number(
            candidate.requested_work_type,
            candidate.project_id) AS business_number
    FROM candidates candidate
)
UPDATE projects project
SET legacy_project_code = issued.legacy_code,
    project_code = issued.business_number,
    project_number_issued_at = COALESCE(project.project_number_issued_at, NOW()),
    project_number_source = 'migration_066_work_register_backfill',
    updated_at = NOW()
FROM issued
WHERE project.project_id = issued.project_id;

UPDATE work_register_project_metadata metadata
SET business_project_number = project.project_code,
    legacy_project_code = project.legacy_project_code,
    metadata_json = COALESCE(metadata.metadata_json, '{}'::jsonb)
        || jsonb_build_object(
            'businessProjectNumber', project.project_code,
            'legacyProjectCode', project.legacy_project_code,
            'projectNumberImmutable', TRUE,
            'projectNumberMigration', '066_immutable_business_project_numbers'),
    updated_at = NOW()
FROM projects project
WHERE project.project_id = metadata.project_id
  AND project.project_number_source IN (
      'module_055d_work_register',
      'migration_066_work_register_backfill');

CREATE OR REPLACE FUNCTION projectpulse066_sync_business_identifier_aliases()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse066_alias$
BEGIN
    INSERT INTO project_business_identifier_aliases (
        project_id, alias_code, alias_type, is_current)
    VALUES (NEW.project_id, NEW.project_code, 'business_number', TRUE)
    ON CONFLICT (alias_code) DO UPDATE
    SET project_id = EXCLUDED.project_id,
        alias_type = 'business_number',
        is_current = TRUE;

    UPDATE project_business_identifier_aliases
    SET is_current = FALSE
    WHERE project_id = NEW.project_id
      AND alias_code <> NEW.project_code;

    IF NULLIF(btrim(COALESCE(NEW.legacy_project_code, '')), '') IS NOT NULL THEN
        INSERT INTO project_business_identifier_aliases (
            project_id, alias_code, alias_type, is_current)
        VALUES (NEW.project_id, NEW.legacy_project_code, 'legacy_work_register', FALSE)
        ON CONFLICT (alias_code) DO UPDATE
        SET project_id = EXCLUDED.project_id,
            alias_type = 'legacy_work_register',
            is_current = FALSE;
    END IF;
    RETURN NEW;
END;
$projectpulse066_alias$;

DROP TRIGGER IF EXISTS trg_projects_066_sync_aliases ON projects;
CREATE TRIGGER trg_projects_066_sync_aliases
AFTER INSERT OR UPDATE OF project_code, legacy_project_code ON projects
FOR EACH ROW EXECUTE FUNCTION projectpulse066_sync_business_identifier_aliases();

INSERT INTO project_business_identifier_aliases (
    project_id, alias_code, alias_type, is_current)
SELECT project_id, project_code, 'business_number', TRUE
FROM projects
WHERE project_number_source IN (
    'module_055d_work_register',
    'migration_066_work_register_backfill')
ON CONFLICT (alias_code) DO UPDATE
SET project_id = EXCLUDED.project_id,
    alias_type = 'business_number',
    is_current = TRUE;

INSERT INTO project_business_identifier_aliases (
    project_id, alias_code, alias_type, is_current)
SELECT project_id, legacy_project_code, 'legacy_work_register', FALSE
FROM projects
WHERE NULLIF(btrim(COALESCE(legacy_project_code, '')), '') IS NOT NULL
ON CONFLICT (alias_code) DO UPDATE
SET project_id = EXCLUDED.project_id,
    alias_type = 'legacy_work_register',
    is_current = FALSE;

CREATE OR REPLACE FUNCTION projectpulse066_sync_work_register_metadata()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse066_metadata$
DECLARE
    current_code TEXT;
    legacy_code TEXT;
BEGIN
    SELECT project.project_code, project.legacy_project_code
    INTO current_code, legacy_code
    FROM projects project
    WHERE project.project_id = NEW.project_id;

    NEW.business_project_number := COALESCE(current_code, NEW.business_project_number);
    NEW.legacy_project_code := COALESCE(legacy_code, NEW.legacy_project_code);
    NEW.metadata_json := COALESCE(NEW.metadata_json, '{}'::jsonb)
        || jsonb_build_object(
            'businessProjectNumber', NEW.business_project_number,
            'legacyProjectCode', NEW.legacy_project_code,
            'projectNumberImmutable', TRUE);
    RETURN NEW;
END;
$projectpulse066_metadata$;

DROP TRIGGER IF EXISTS trg_work_register_metadata_066_numbers
    ON work_register_project_metadata;
CREATE TRIGGER trg_work_register_metadata_066_numbers
BEFORE INSERT OR UPDATE ON work_register_project_metadata
FOR EACH ROW EXECUTE FUNCTION projectpulse066_sync_work_register_metadata();

DO $projectpulse066_commit_sync$
BEGIN
    IF to_regclass('public.work_register_intake_commits') IS NOT NULL THEN
        EXECUTE $sql$
            CREATE OR REPLACE FUNCTION projectpulse066_sync_intake_commit_number()
            RETURNS TRIGGER
            LANGUAGE plpgsql
            AS $function$
            DECLARE
                current_code TEXT;
                legacy_code TEXT;
            BEGIN
                SELECT project.project_code, project.legacy_project_code
                INTO current_code, legacy_code
                FROM projects project
                WHERE project.project_id = NEW.project_id;
                NEW.project_code := COALESCE(current_code, NEW.project_code);
                NEW.commit_summary_json := COALESCE(NEW.commit_summary_json, '{}'::jsonb)
                    || jsonb_build_object(
                        'businessProjectNumber', current_code,
                        'legacyProjectCode', legacy_code,
                        'projectNumberImmutable', TRUE);
                RETURN NEW;
            END;
            $function$;
        $sql$;
        EXECUTE 'DROP TRIGGER IF EXISTS trg_work_register_intake_commit_066_number ON work_register_intake_commits';
        EXECUTE 'CREATE TRIGGER trg_work_register_intake_commit_066_number BEFORE INSERT OR UPDATE ON work_register_intake_commits FOR EACH ROW EXECUTE FUNCTION projectpulse066_sync_intake_commit_number()';

        EXECUTE $sql$
            UPDATE work_register_intake_commits committed
            SET project_code = project.project_code,
                commit_summary_json = COALESCE(committed.commit_summary_json, '{}'::jsonb)
                    || jsonb_build_object(
                        'businessProjectNumber', project.project_code,
                        'legacyProjectCode', project.legacy_project_code,
                        'projectNumberImmutable', TRUE)
            FROM projects project
            WHERE project.project_id = committed.project_id
              AND committed.project_code IS DISTINCT FROM project.project_code
        $sql$;
    END IF;
END;
$projectpulse066_commit_sync$;

-- Preserve the established 055D commit implementation behind a wrapper that
-- returns the business number actually stored by the projects INSERT trigger.
-- This avoids retaining the temporary WR-* value in the browser response while
-- keeping the original commit function, validation, and audit behavior intact.
DO $projectpulse066_commit_wrapper$
BEGIN
    IF to_regprocedure('public.projectpulse055d4d_commit_intake_package_legacy_066(uuid,uuid)') IS NULL
       AND to_regprocedure('public.projectpulse055d4d_commit_intake_package(uuid,uuid)') IS NOT NULL THEN
        EXECUTE 'ALTER FUNCTION projectpulse055d4d_commit_intake_package(UUID, UUID) RENAME TO projectpulse055d4d_commit_intake_package_legacy_066';
    END IF;

    IF to_regprocedure('public.projectpulse055d4d_commit_intake_package_legacy_066(uuid,uuid)') IS NOT NULL THEN
        EXECUTE $wrapper$
            CREATE OR REPLACE FUNCTION projectpulse055d4d_commit_intake_package(
                p_intake_package_id UUID,
                p_actor_user_id UUID)
            RETURNS JSONB
            LANGUAGE plpgsql
            AS $function$
            DECLARE
                result JSONB;
                committed_project_id UUID;
                current_code TEXT;
                legacy_code TEXT;
            BEGIN
                result := projectpulse055d4d_commit_intake_package_legacy_066(
                    p_intake_package_id,
                    p_actor_user_id);

                BEGIN
                    committed_project_id := NULLIF(result->>'projectId', '')::UUID;
                EXCEPTION WHEN others THEN
                    committed_project_id := NULL;
                END;

                IF committed_project_id IS NOT NULL THEN
                    SELECT project.project_code, project.legacy_project_code
                    INTO current_code, legacy_code
                    FROM projects project
                    WHERE project.project_id = committed_project_id;

                    IF current_code IS NOT NULL THEN
                        result := result || jsonb_build_object(
                            'projectCode', current_code,
                            'businessProjectNumber', current_code,
                            'legacyProjectCode', COALESCE(legacy_code, ''),
                            'projectNumberImmutable', TRUE);
                    END IF;
                END IF;

                RETURN result;
            END;
            $function$;
        $wrapper$;
    END IF;
END;
$projectpulse066_commit_wrapper$;

CREATE OR REPLACE FUNCTION projectpulse_resolve_project_identifier(identifier TEXT)
RETURNS UUID
LANGUAGE SQL
STABLE
AS $projectpulse066_resolve$
    SELECT resolved.project_id
    FROM (
        SELECT project.project_id, 0 AS precedence
        FROM projects project
        WHERE project.project_id::text = btrim(COALESCE(identifier, ''))
           OR upper(project.project_code) = upper(btrim(COALESCE(identifier, '')))
        UNION ALL
        SELECT alias.project_id, 1 AS precedence
        FROM project_business_identifier_aliases alias
        WHERE upper(alias.alias_code) = upper(btrim(COALESCE(identifier, '')))
    ) resolved
    ORDER BY resolved.precedence
    LIMIT 1;
$projectpulse066_resolve$;

CREATE OR REPLACE FUNCTION projectpulse066_guard_project_number_immutability()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse066_immutable$
BEGIN
    IF OLD.project_code IS DISTINCT FROM NEW.project_code THEN
        RAISE EXCEPTION 'The issued business project number is immutable. Preserve the UUID and use project_business_identifier_aliases for historical lookup.';
    END IF;
    IF OLD.legacy_project_code IS DISTINCT FROM NEW.legacy_project_code THEN
        RAISE EXCEPTION 'The legacy Work Register project-code alias is immutable.';
    END IF;
    RETURN NEW;
END;
$projectpulse066_immutable$;

DROP TRIGGER IF EXISTS trg_projects_066_number_immutable ON projects;
CREATE TRIGGER trg_projects_066_number_immutable
BEFORE UPDATE OF project_code, legacy_project_code ON projects
FOR EACH ROW EXECUTE FUNCTION projectpulse066_guard_project_number_immutability();

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '066_immutable_business_project_numbers',
    'Issue immutable PRO, SR, IQS, INT, and PRES business project numbers while retaining UUID identity and WR aliases',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
