-- Rollback for ProjectPulse migration 066.
-- Intended for controlled Test validation before post-migration business use.

BEGIN;

DROP TRIGGER IF EXISTS trg_projects_issued_project_number_guard_insert ON projects;
DROP TRIGGER IF EXISTS trg_projects_issued_project_number_immutable ON projects;
DROP TRIGGER IF EXISTS trg_project_code_aliases_immutable ON project_code_aliases;

-- Restore the former Work Register code for records that were backfilled from a
-- retained alias. Projects created natively with a permanent number have no
-- legacy alias and retain their issued number.
DO $projectpulse066_restore_aliases$
DECLARE
    item RECORD;
BEGIN
    IF to_regclass('public.project_code_aliases') IS NULL THEN RETURN; END IF;

    FOR item IN
        SELECT DISTINCT ON (alias.project_id)
            alias.project_id,
            alias.alias_code,
            alias.issued_project_code
        FROM project_code_aliases alias
        WHERE alias.source_migration = '066_immutable_project_numbers'
          AND alias.alias_type IN ('legacy_work_register_code', 'legacy_project_code')
        ORDER BY alias.project_id, alias.created_at
    LOOP
        UPDATE projects
           SET project_code = item.alias_code,
               updated_at = NOW()
         WHERE project_id = item.project_id
           AND project_code = item.issued_project_code;

        UPDATE work_register_intake_commits
           SET project_code = item.alias_code,
               commit_summary_json = (coalesce(commit_summary_json, '{}'::jsonb)
                    - 'legacyProjectCode'
                    - 'projectNumberImmutable')
                    || jsonb_build_object('projectCode', item.alias_code)
         WHERE project_id = item.project_id;

        UPDATE work_register_project_metadata
           SET metadata_json = coalesce(metadata_json, '{}'::jsonb)
                    - 'projectNumberIssuedAt'
                    - 'projectNumberSource'
                    - 'legacyProjectCode'
                    || jsonb_build_object('projectCode', item.alias_code),
               updated_at = NOW()
         WHERE project_id = item.project_id;
    END LOOP;
END;
$projectpulse066_restore_aliases$;

DROP FUNCTION IF EXISTS public.projectpulse055d4d_commit_intake_package(uuid,uuid);
DO $projectpulse066_restore_final_save$
BEGIN
    IF to_regprocedure('public.projectpulse055d4d_commit_intake_package_legacy_066(uuid,uuid)') IS NOT NULL THEN
        ALTER FUNCTION public.projectpulse055d4d_commit_intake_package_legacy_066(uuid,uuid)
            RENAME TO projectpulse055d4d_commit_intake_package;
    END IF;
END;
$projectpulse066_restore_final_save$;

DROP FUNCTION IF EXISTS projectpulse_resolve_project_id(TEXT);
DROP FUNCTION IF EXISTS projectpulse066_issue_project_number(UUID,TEXT,UUID);
DROP FUNCTION IF EXISTS projectpulse066_guard_issued_project_number();
DROP FUNCTION IF EXISTS projectpulse066_uuid_or_null(TEXT);
DROP FUNCTION IF EXISTS projectpulse066_generate_project_code(UUID,TEXT,INTEGER);
DROP FUNCTION IF EXISTS projectpulse066_project_prefix(TEXT);
DROP FUNCTION IF EXISTS projectpulse066_canonical_work_type(TEXT);
DROP FUNCTION IF EXISTS projectpulse066_block_project_code_alias_mutation();

DROP TABLE IF EXISTS project_code_aliases;

DELETE FROM schema_migrations
WHERE migration_id = '066_immutable_project_numbers';

COMMIT;
