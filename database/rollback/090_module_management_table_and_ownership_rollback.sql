-- Guarded rollback for Pulse migration 090.
-- Refuses to overwrite any module owner changed after migration 090.

BEGIN;

DO $projectpulse090_rollback$
DECLARE
    changed_after_migration INTEGER;
BEGIN
    IF to_regclass('public.module_catalog_ownership_090_evidence') IS NULL THEN
        RETURN;
    END IF;

    SELECT COUNT(*)
    INTO changed_after_migration
    FROM module_catalog_ownership_090_evidence evidence
    JOIN scoped_role_policy_modules module
      ON module.module_code = evidence.module_code
    WHERE module.owner_user_id IS DISTINCT FROM evidence.assigned_owner_user_id
       OR module.owner_revision_number IS DISTINCT FROM evidence.assigned_owner_revision_number;

    IF changed_after_migration <> 0 THEN
        RAISE EXCEPTION
            'Rollback 090 refused: % module owner record(s) changed after migration 090.',
            changed_after_migration;
    END IF;

    UPDATE scoped_role_policy_modules module
    SET owner_user_id = evidence.previous_owner_user_id,
        owner_revision_number = evidence.previous_owner_revision_number,
        owner_updated_at = evidence.previous_owner_updated_at,
        owner_updated_by_user_id = evidence.previous_owner_updated_by_user_id
    FROM module_catalog_ownership_090_evidence evidence
    WHERE module.module_code = evidence.module_code;
END;
$projectpulse090_rollback$;

DELETE FROM schema_migrations
WHERE migration_id = '090_module_management_table_and_ownership';

DROP TABLE IF EXISTS module_catalog_ownership_090_evidence;

-- Ownership columns and immutable audit evidence are intentionally retained.
COMMIT;
