-- Roll back ProjectPulse migration 066.
-- Restores prior WR-* project codes for Work Register-created projects.

BEGIN;

DROP TRIGGER IF EXISTS trg_projects_066_number_immutable ON projects;
DROP TRIGGER IF EXISTS trg_projects_066_sync_aliases ON projects;
DROP TRIGGER IF EXISTS trg_projects_066_assign_business_number ON projects;
DROP TRIGGER IF EXISTS trg_work_register_metadata_066_numbers ON work_register_project_metadata;

DO $projectpulse066_commit_rollback$
BEGIN
    IF to_regclass('public.work_register_intake_commits') IS NOT NULL THEN
        EXECUTE 'DROP TRIGGER IF EXISTS trg_work_register_intake_commit_066_number ON work_register_intake_commits';
    END IF;
END;
$projectpulse066_commit_rollback$;

UPDATE projects
SET project_code = legacy_project_code,
    updated_at = NOW()
WHERE project_number_source IN (
        'module_055d_work_register',
        'migration_066_work_register_backfill')
  AND NULLIF(btrim(COALESCE(legacy_project_code, '')), '') IS NOT NULL;

DO $projectpulse066_commit_restore$
BEGIN
    IF to_regclass('public.work_register_intake_commits') IS NOT NULL THEN
        EXECUTE $sql$
            UPDATE work_register_intake_commits committed
            SET project_code = project.project_code,
                commit_summary_json = COALESCE(committed.commit_summary_json, '{}'::jsonb) - 'businessProjectNumber' - 'legacyProjectCode' - 'projectNumberImmutable'
            FROM projects project
            WHERE project.project_id = committed.project_id
        $sql$;
    END IF;
END;
$projectpulse066_commit_restore$;

DO $projectpulse066_commit_wrapper_rollback$
BEGIN
    IF to_regprocedure('public.projectpulse055d4d_commit_intake_package_legacy_066(uuid,uuid)') IS NOT NULL THEN
        EXECUTE 'DROP FUNCTION IF EXISTS projectpulse055d4d_commit_intake_package(UUID, UUID)';
        EXECUTE 'ALTER FUNCTION projectpulse055d4d_commit_intake_package_legacy_066(UUID, UUID) RENAME TO projectpulse055d4d_commit_intake_package';
    END IF;
END;
$projectpulse066_commit_wrapper_rollback$;

DROP FUNCTION IF EXISTS projectpulse066_guard_project_number_immutability();
DROP FUNCTION IF EXISTS projectpulse066_sync_intake_commit_number();
DROP FUNCTION IF EXISTS projectpulse066_sync_work_register_metadata();
DROP FUNCTION IF EXISTS projectpulse066_sync_business_identifier_aliases();
DROP FUNCTION IF EXISTS projectpulse066_assign_business_project_number();
DROP FUNCTION IF EXISTS projectpulse066_issue_business_project_number(TEXT, UUID);
DROP FUNCTION IF EXISTS projectpulse066_work_type_from_description(TEXT);
DROP FUNCTION IF EXISTS projectpulse066_project_number_prefix(TEXT);
DROP FUNCTION IF EXISTS projectpulse066_canonical_work_type(TEXT);
DROP FUNCTION IF EXISTS projectpulse_resolve_project_identifier(TEXT);

DROP TABLE IF EXISTS project_business_identifier_aliases;

ALTER TABLE work_register_project_metadata
    DROP COLUMN IF EXISTS business_project_number,
    DROP COLUMN IF EXISTS legacy_project_code;

ALTER TABLE projects
    DROP COLUMN IF EXISTS project_number_source,
    DROP COLUMN IF EXISTS project_number_issued_at,
    DROP COLUMN IF EXISTS legacy_project_code;

DELETE FROM schema_migrations
WHERE migration_id = '066_immutable_business_project_numbers';

COMMIT;
