-- ProjectPulse migration 066
-- Immutable human-readable project numbers for Module 055D, Module 055C, and
-- the guided Module 040 closeout workflow.
--
-- The UUID project_id remains the relational identity. This migration issues a
-- permanent business identifier to Work Register projects, preserves the former
-- WR-* value as a resolvable legacy alias, synchronizes intake-commit evidence,
-- and prevents later application/import changes to an issued number.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

DO $projectpulse066_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.projects') IS NULL
       OR to_regclass('public.work_register_project_metadata') IS NULL
       OR to_regclass('public.work_register_intake_packages') IS NULL
       OR to_regclass('public.work_register_intake_commits') IS NULL
       OR to_regprocedure('public.projectpulse055d4d_commit_intake_package(uuid,uuid)') IS NULL THEN
        RAISE EXCEPTION 'Migration 066 requires the ProjectPulse Work Register and Module 055D final-save foundation.';
    END IF;
END;
$projectpulse066_prerequisites$;

CREATE TABLE IF NOT EXISTS project_code_aliases (
    project_code_alias_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE RESTRICT,
    alias_code VARCHAR(100) NOT NULL UNIQUE,
    alias_type VARCHAR(50) NOT NULL DEFAULT 'legacy_work_register_code',
    issued_project_code VARCHAR(100) NOT NULL,
    source_migration VARCHAR(80) NOT NULL DEFAULT '066_immutable_project_numbers',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_project_code_alias_type CHECK (
        alias_type IN ('legacy_work_register_code', 'legacy_project_code', 'external_reference')
    )
);

CREATE INDEX IF NOT EXISTS idx_project_code_aliases_project
    ON project_code_aliases(project_id, created_at DESC);

CREATE OR REPLACE FUNCTION projectpulse066_block_project_code_alias_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse066_alias_immutable$
BEGIN
    RAISE EXCEPTION 'Project-number alias evidence is immutable.';
END;
$projectpulse066_alias_immutable$;

DROP TRIGGER IF EXISTS trg_project_code_aliases_immutable ON project_code_aliases;
CREATE TRIGGER trg_project_code_aliases_immutable
BEFORE UPDATE OR DELETE ON project_code_aliases
FOR EACH ROW EXECUTE FUNCTION projectpulse066_block_project_code_alias_mutation();

CREATE OR REPLACE FUNCTION projectpulse066_canonical_work_type(value TEXT)
RETURNS TEXT
LANGUAGE sql
IMMUTABLE
AS $projectpulse066_work_type$
    SELECT CASE regexp_replace(lower(btrim(coalesce(value, ''))), '[^a-z0-9]+', '', 'g')
        WHEN 'servicerequest' THEN 'SERVICE_REQUEST'
        WHEN 'sr' THEN 'SERVICE_REQUEST'
        WHEN 'iqs' THEN 'IQS'
        WHEN 'internalproject' THEN 'INTERNAL'
        WHEN 'internal' THEN 'INTERNAL'
        WHEN 'presales' THEN 'PRE_SALES'
        WHEN 'presale' THEN 'PRE_SALES'
        WHEN 'pres' THEN 'PRE_SALES'
        ELSE 'PROJECT'
    END;
$projectpulse066_work_type$;

CREATE OR REPLACE FUNCTION projectpulse066_project_prefix(value TEXT)
RETURNS TEXT
LANGUAGE sql
IMMUTABLE
AS $projectpulse066_prefix$
    SELECT CASE projectpulse066_canonical_work_type(value)
        WHEN 'SERVICE_REQUEST' THEN 'SR'
        WHEN 'IQS' THEN 'IQS'
        WHEN 'INTERNAL' THEN 'INT'
        WHEN 'PRE_SALES' THEN 'PRES'
        ELSE 'PRO'
    END;
$projectpulse066_prefix$;

CREATE OR REPLACE FUNCTION projectpulse066_generate_project_code(
    project_id UUID,
    requested_work_type TEXT,
    collision_attempt INTEGER DEFAULT 0
)
RETURNS TEXT
LANGUAGE plpgsql
IMMUTABLE
AS $projectpulse066_generate$
DECLARE
    suffix TEXT;
BEGIN
    IF collision_attempt <= 0 THEN
        suffix := upper(substr(replace(project_id::text, '-', ''), 1, 8));
    ELSE
        suffix := upper(substr(encode(
            digest(project_id::text || ':' || collision_attempt::text, 'sha256'),
            'hex'), 1, 8));
    END IF;
    RETURN projectpulse066_project_prefix(requested_work_type) || '-' || suffix;
END;
$projectpulse066_generate$;

CREATE OR REPLACE FUNCTION projectpulse066_uuid_or_null(value TEXT)
RETURNS UUID
LANGUAGE plpgsql
IMMUTABLE
AS $projectpulse066_uuid$
BEGIN
    IF value IS NULL OR btrim(value) = '' THEN RETURN NULL; END IF;
    RETURN value::uuid;
EXCEPTION WHEN others THEN
    RETURN NULL;
END;
$projectpulse066_uuid$;

CREATE OR REPLACE FUNCTION projectpulse066_guard_issued_project_number()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse066_project_number_guard$
DECLARE
    issuance_authorized BOOLEAN :=
        coalesce(current_setting('projectpulse.project_number_issuance', TRUE), '') = 'on';
BEGIN
    IF TG_OP = 'INSERT' THEN
        IF NEW.project_code ~ '^(PRO|SR|IQS|INT|PRES)-[A-Z0-9]{8}$'
           AND NOT issuance_authorized THEN
            RAISE EXCEPTION 'ProjectPulse project numbers may be issued only by the governed Module 055D database workflow.';
        END IF;
        RETURN NEW;
    END IF;

    IF NEW.project_code IS NOT DISTINCT FROM OLD.project_code THEN
        RETURN NEW;
    END IF;

    IF OLD.project_code ~ '^(PRO|SR|IQS|INT|PRES)-[A-Z0-9]{8}$' THEN
        RAISE EXCEPTION 'Issued ProjectPulse project numbers are immutable.';
    END IF;

    IF NEW.project_code ~ '^(PRO|SR|IQS|INT|PRES)-[A-Z0-9]{8}$'
       AND NOT issuance_authorized THEN
        RAISE EXCEPTION 'ProjectPulse project numbers may be issued only by the governed Module 055D database workflow.';
    END IF;

    RETURN NEW;
END;
$projectpulse066_project_number_guard$;

DROP TRIGGER IF EXISTS trg_projects_issued_project_number_guard_insert ON projects;
CREATE TRIGGER trg_projects_issued_project_number_guard_insert
BEFORE INSERT ON projects
FOR EACH ROW EXECUTE FUNCTION projectpulse066_guard_issued_project_number();

DROP TRIGGER IF EXISTS trg_projects_issued_project_number_immutable ON projects;
CREATE TRIGGER trg_projects_issued_project_number_immutable
BEFORE UPDATE OF project_code ON projects
FOR EACH ROW EXECUTE FUNCTION projectpulse066_guard_issued_project_number();

CREATE OR REPLACE FUNCTION projectpulse066_issue_project_number(
    target_project_id UUID,
    requested_work_type TEXT,
    actor_user_id UUID DEFAULT NULL
)
RETURNS TEXT
LANGUAGE plpgsql
AS $projectpulse066_issue$
DECLARE
    old_code TEXT;
    new_code TEXT;
    attempt INTEGER := 0;
BEGIN
    SELECT project_code
      INTO old_code
      FROM projects
     WHERE project_id = target_project_id
     FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Project % was not found while issuing a permanent project number.', target_project_id;
    END IF;

    IF old_code ~ '^(PRO|SR|IQS|INT|PRES)-[A-Z0-9]{8}$' THEN
        RETURN old_code;
    END IF;

    LOOP
        new_code := projectpulse066_generate_project_code(
            target_project_id,
            requested_work_type,
            attempt);
        EXIT WHEN NOT EXISTS (
            SELECT 1
              FROM projects
             WHERE project_code = new_code
               AND project_id <> target_project_id
        );
        attempt := attempt + 1;
        IF attempt > 50 THEN
            RAISE EXCEPTION 'A unique permanent project number could not be generated for project %.', target_project_id;
        END IF;
    END LOOP;

    IF btrim(coalesce(old_code, '')) <> '' THEN
        INSERT INTO project_code_aliases (
            project_id,
            alias_code,
            alias_type,
            issued_project_code,
            source_migration
        )
        VALUES (
            target_project_id,
            old_code,
            CASE WHEN old_code LIKE 'WR-%'
                 THEN 'legacy_work_register_code'
                 ELSE 'legacy_project_code' END,
            new_code,
            '066_immutable_project_numbers'
        )
        ON CONFLICT (alias_code) DO NOTHING;
    END IF;

    PERFORM set_config('projectpulse.project_number_issuance', 'on', TRUE);

    UPDATE projects
       SET project_code = new_code,
           updated_at = NOW()
     WHERE project_id = target_project_id;

    UPDATE work_register_intake_commits
       SET project_code = new_code,
           commit_summary_json = coalesce(commit_summary_json, '{}'::jsonb)
             || jsonb_build_object(
                    'projectCode', new_code,
                    'legacyProjectCode', nullif(old_code, '')
                )
     WHERE project_id = target_project_id;

    UPDATE work_register_project_metadata
       SET metadata_json = coalesce(metadata_json, '{}'::jsonb)
             || jsonb_build_object(
                    'projectCode', new_code,
                    'legacyProjectCode', nullif(old_code, ''),
                    'projectNumberIssuedAt', NOW(),
                    'projectNumberSource', 'Module 055D governed database issuance'
                ),
           updated_at = NOW()
     WHERE project_id = target_project_id;

    IF to_regclass('public.work_register_change_history') IS NOT NULL THEN
        INSERT INTO work_register_change_history (
            work_register_change_history_id,
            source_table,
            work_id,
            action,
            change_summary,
            changed_fields_csv,
            changed_by_user_id,
            old_value_json,
            new_value_json,
            changed_at
        )
        VALUES (
            gen_random_uuid(),
            'projects',
            target_project_id,
            'permanent_project_number_issued',
            'Issued immutable project number ' || new_code ||
                CASE WHEN btrim(coalesce(old_code, '')) <> ''
                     THEN ' and retained ' || old_code || ' as a legacy alias.'
                     ELSE '.' END,
            'project_code,legacy_project_code',
            actor_user_id,
            jsonb_build_object('projectCode', old_code),
            jsonb_build_object(
                'projectCode', new_code,
                'legacyProjectCode', nullif(old_code, ''),
                'immutable', TRUE),
            NOW()
        );
    END IF;

    PERFORM set_config('projectpulse.project_number_issuance', 'off', TRUE);
    RETURN new_code;
END;
$projectpulse066_issue$;

CREATE OR REPLACE FUNCTION projectpulse_resolve_project_id(project_reference TEXT)
RETURNS UUID
LANGUAGE sql
STABLE
AS $projectpulse066_resolve$
    SELECT project_id
      FROM (
            SELECT p.project_id, 0 AS priority
              FROM projects p
             WHERE lower(p.project_code) = lower(btrim(coalesce(project_reference, '')))
            UNION ALL
            SELECT alias.project_id, 1 AS priority
              FROM project_code_aliases alias
             WHERE lower(alias.alias_code) = lower(btrim(coalesce(project_reference, '')))
      ) candidates
     ORDER BY priority
     LIMIT 1;
$projectpulse066_resolve$;

-- Backfill only records created by the Work Register intake workflow. Existing
-- non-Work-Register business codes are left unchanged.
DO $projectpulse066_backfill$
DECLARE
    item RECORD;
BEGIN
    FOR item IN
        SELECT
            project.project_id,
            coalesce(
                nullif(metadata.requested_work_type, ''),
                nullif(package.requested_work_type, ''),
                'Project') AS requested_work_type,
            commit.committed_by_user_id AS actor_user_id
        FROM projects project
        LEFT JOIN work_register_project_metadata metadata
          ON metadata.project_id = project.project_id
        LEFT JOIN work_register_intake_packages package
          ON package.work_register_intake_package_id = metadata.work_register_intake_package_id
        LEFT JOIN work_register_intake_commits commit
          ON commit.project_id = project.project_id
        WHERE (metadata.project_id IS NOT NULL OR commit.project_id IS NOT NULL)
          AND project.project_code !~ '^(PRO|SR|IQS|INT|PRES)-[A-Z0-9]{8}$'
        ORDER BY project.created_at, project.project_id
    LOOP
        PERFORM projectpulse066_issue_project_number(
            item.project_id,
            item.requested_work_type,
            item.actor_user_id);
    END LOOP;
END;
$projectpulse066_backfill$;

-- Preserve the exact pre-066 final-save implementation once, then place a
-- compatibility wrapper at the historic function name. The wrapper makes the
-- database-issued number authoritative for both current and future callers.
DO $projectpulse066_wrap_final_save$
BEGIN
    IF to_regprocedure('public.projectpulse055d4d_commit_intake_package_legacy_066(uuid,uuid)') IS NULL THEN
        ALTER FUNCTION public.projectpulse055d4d_commit_intake_package(uuid,uuid)
            RENAME TO projectpulse055d4d_commit_intake_package_legacy_066;
    END IF;
END;
$projectpulse066_wrap_final_save$;

CREATE OR REPLACE FUNCTION projectpulse055d4d_commit_intake_package(
    p_intake_package_id UUID,
    p_actor_user_id UUID
)
RETURNS JSONB
LANGUAGE plpgsql
AS $projectpulse066_final_save_wrapper$
DECLARE
    result_payload JSONB;
    target_project_id UUID;
    requested_work_type TEXT;
    old_code TEXT;
    issued_code TEXT;
    result_message TEXT;
BEGIN
    result_payload := projectpulse055d4d_commit_intake_package_legacy_066(
        p_intake_package_id,
        p_actor_user_id);

    IF coalesce(result_payload->>'status', '') NOT IN ('committed', 'already_committed') THEN
        RETURN result_payload;
    END IF;

    target_project_id := projectpulse066_uuid_or_null(result_payload->>'projectId');
    IF target_project_id IS NULL THEN
        RETURN result_payload;
    END IF;

    SELECT project_code
      INTO old_code
      FROM projects
     WHERE project_id = target_project_id;

    SELECT coalesce(
               nullif(metadata.requested_work_type, ''),
               nullif(package.requested_work_type, ''),
               'Project')
      INTO requested_work_type
      FROM projects project
      LEFT JOIN work_register_project_metadata metadata
        ON metadata.project_id = project.project_id
      LEFT JOIN work_register_intake_packages package
        ON package.work_register_intake_package_id = metadata.work_register_intake_package_id
     WHERE project.project_id = target_project_id;

    issued_code := projectpulse066_issue_project_number(
        target_project_id,
        coalesce(requested_work_type, 'Project'),
        p_actor_user_id);

    result_message := coalesce(result_payload->>'message', 'Work Register project created.');
    IF btrim(coalesce(old_code, '')) <> '' AND old_code <> issued_code THEN
        result_message := replace(result_message, old_code, issued_code);
    END IF;

    RETURN result_payload
        || jsonb_build_object(
            'projectCode', issued_code,
            'legacyProjectCode', CASE WHEN old_code <> issued_code THEN old_code ELSE NULL END,
            'projectNumberImmutable', TRUE,
            'message', result_message);
END;
$projectpulse066_final_save_wrapper$;

DO $projectpulse066_assertions$
DECLARE
    invalid_count INTEGER;
    unsynchronized_count INTEGER;
BEGIN
    SELECT COUNT(*)
      INTO invalid_count
      FROM projects project
      JOIN work_register_project_metadata metadata
        ON metadata.project_id = project.project_id
     WHERE project.project_code !~ '^(PRO|SR|IQS|INT|PRES)-[A-Z0-9]{8}$';

    IF invalid_count <> 0 THEN
        RAISE EXCEPTION 'Migration 066 invariant failed: % Work Register project(s) lack a permanent project number.', invalid_count;
    END IF;

    SELECT COUNT(*)
      INTO unsynchronized_count
      FROM work_register_intake_commits commit
      JOIN projects project ON project.project_id = commit.project_id
     WHERE commit.project_code IS DISTINCT FROM project.project_code;

    IF unsynchronized_count <> 0 THEN
        RAISE EXCEPTION 'Migration 066 invariant failed: % intake commit(s) are not synchronized with the permanent project number.', unsynchronized_count;
    END IF;
END;
$projectpulse066_assertions$;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '066_immutable_project_numbers',
    'Issue immutable PRO/SR/IQS/INT/PRES project numbers, preserve WR aliases, synchronize 055D/055C/040 evidence, and prevent later mutation',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
