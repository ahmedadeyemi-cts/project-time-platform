-- Guarded rollback for Pulse migration 091.
-- Refuses an unprovable owner rollback and never overwrites a later owner change.

BEGIN;

DO $projectpulse091_rollback$
DECLARE
    changed_after_migration INTEGER;
BEGIN
    IF to_regclass('public.module_catalog_ownership_091_evidence') IS NULL THEN
        RETURN;
    END IF;

    SELECT COUNT(*)
    INTO changed_after_migration
    FROM module_catalog_ownership_091_evidence evidence
    JOIN scoped_role_policy_modules module
      ON module.module_code = evidence.module_code
    WHERE module.owner_user_id IS DISTINCT FROM evidence.assigned_owner_user_id
       OR module.owner_revision_number IS DISTINCT FROM evidence.assigned_owner_revision_number;

    IF changed_after_migration <> 0 THEN
        RAISE EXCEPTION
            'Rollback 091 refused: % module owner record(s) changed after migration 091; refusing an unprovable owner rollback.',
            changed_after_migration;
    END IF;

    UPDATE scoped_role_policy_modules module
    SET owner_user_id = evidence.previous_owner_user_id,
        owner_revision_number = evidence.previous_owner_revision_number,
        owner_updated_at = evidence.previous_owner_updated_at,
        owner_updated_by_user_id = evidence.previous_owner_updated_by_user_id
    FROM module_catalog_ownership_091_evidence evidence
    WHERE module.module_code = evidence.module_code
      AND module.owner_user_id IS NOT DISTINCT FROM evidence.assigned_owner_user_id
      AND module.owner_revision_number IS NOT DISTINCT FROM evidence.assigned_owner_revision_number;
END;
$projectpulse091_rollback$;

DELETE FROM schema_migrations
WHERE migration_id = '091_module_management_owner_storage_repair';

DROP TABLE IF EXISTS module_catalog_ownership_091_evidence;

-- Ownership columns, constraints, index, and immutable audit events are retained.
COMMIT;
