-- Roll back ProjectPulse migration 040 scoped RBAC foundation.
-- This rollback is intentionally fail-closed after any policy version beyond
-- the seeded workbook baseline has been published.

BEGIN;

DO $projectpulse040_rollback_guard$
DECLARE
    v_extra_versions INTEGER;
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id = '040_scoped_role_policy_versions'
    ) THEN
        RAISE EXCEPTION
            'Migration 040 is not registered and cannot be rolled back.';
    END IF;

    SELECT COUNT(*)
    INTO v_extra_versions
    FROM scoped_role_policy_versions
    WHERE version_number > 1;

    IF v_extra_versions > 0 THEN
        RAISE EXCEPTION
            'Migration 040 rollback blocked: % policy version(s) exist beyond the workbook baseline.',
            v_extra_versions;
    END IF;
END;
$projectpulse040_rollback_guard$;

DROP VIEW IF EXISTS scoped_role_policy_effective_grants;

DROP TRIGGER IF EXISTS trg_projectpulse040_time_audit_immutable
ON scoped_time_correction_events;
DROP TRIGGER IF EXISTS trg_projectpulse040_approval_audit_immutable
ON scoped_approval_stage_events;
DROP TRIGGER IF EXISTS trg_projectpulse040_policy_audit_immutable
ON scoped_role_policy_audit_events;
DROP TRIGGER IF EXISTS trg_projectpulse040_published_grants_immutable
ON scoped_role_policy_grants;

DROP FUNCTION IF EXISTS projectpulse040_block_published_grant_mutation();
DROP FUNCTION IF EXISTS projectpulse040_block_immutable_audit_mutation();

DROP TABLE IF EXISTS scoped_time_correction_events;
DROP TABLE IF EXISTS scoped_approval_stage_events;
DROP TABLE IF EXISTS scoped_role_policy_audit_events;
DROP TABLE IF EXISTS scoped_role_policy_grants;
DROP TABLE IF EXISTS scoped_role_policy_versions;
DROP TABLE IF EXISTS scoped_role_policy_scopes;
DROP TABLE IF EXISTS scoped_role_policy_actions;
DROP TABLE IF EXISTS scoped_role_policy_modules;

DELETE FROM schema_migrations
WHERE migration_id = '040_scoped_role_policy_versions';

COMMIT;
