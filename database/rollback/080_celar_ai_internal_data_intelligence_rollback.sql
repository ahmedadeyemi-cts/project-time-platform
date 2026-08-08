BEGIN;

-- This row is fully reproducible from migration 080 and may be removed by its
-- rollback. Any operator-created or independently verified alias still blocks
-- destructive rollback below.
DO $projectpulse080_remove_seed$
BEGIN
    IF to_regclass('public.celar_ai_identity_aliases') IS NOT NULL THEN
        DELETE FROM celar_ai_identity_aliases
        WHERE verification_source = 'migration_080_known_directory_correction'
          AND created_by_user_id IS NULL;
    END IF;
END;
$projectpulse080_remove_seed$;

DO $projectpulse080_rollback_guard$
BEGIN
    IF to_regclass('public.celar_ai_identity_aliases') IS NOT NULL
       AND EXISTS (SELECT 1 FROM celar_ai_identity_aliases) THEN
        RAISE EXCEPTION 'Rollback refused: verified Celar AI identity-alias evidence exists. Deactivate or migrate the governed evidence before rollback.';
    END IF;
END;
$projectpulse080_rollback_guard$;

DROP TRIGGER IF EXISTS trg_celar_ai_identity_alias_touch ON celar_ai_identity_aliases;
DROP FUNCTION IF EXISTS projectpulse080_touch_identity_alias();
DROP TABLE IF EXISTS celar_ai_identity_aliases;
DROP INDEX IF EXISTS ix_celar_ai_current_roster_person_project;

DELETE FROM schema_migrations
WHERE migration_id = '080_celar_ai_internal_data_intelligence';

COMMIT;
