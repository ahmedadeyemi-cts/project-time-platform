-- Rollback for Pulse migration 089.
--
-- Guardrail: a later published role-policy version must be handled separately.
-- The rollback refuses to overwrite newer authorization decisions.

BEGIN;

DO $projectpulse089_rollback_policy$
DECLARE
    previous_id UUID;
    replacement_id UUID;
    current_published_id UUID;
BEGIN
    IF to_regclass('public.module_catalog_reconciliation_089_policy_versions') IS NULL THEN
        RETURN;
    END IF;

    SELECT
        previous_policy_version_id,
        replacement_policy_version_id
    INTO
        previous_id,
        replacement_id
    FROM module_catalog_reconciliation_089_policy_versions
    WHERE singleton_key = TRUE;

    IF previous_id IS NULL OR replacement_id IS NULL THEN
        RETURN;
    END IF;

    SELECT policy_version_id
    INTO current_published_id
    FROM scoped_role_policy_versions
    WHERE policy_status = 'PUBLISHED'
    ORDER BY version_number DESC
    LIMIT 1;

    IF current_published_id = replacement_id THEN
        UPDATE scoped_role_policy_versions
        SET policy_status = 'RETIRED',
            retired_at = NOW()
        WHERE policy_version_id = replacement_id;

        UPDATE scoped_role_policy_versions
        SET policy_status = 'PUBLISHED',
            retired_at = NULL
        WHERE policy_version_id = previous_id;

        IF to_regclass('public.scoped_role_policy_audit_events') IS NOT NULL THEN
            INSERT INTO scoped_role_policy_audit_events (
                policy_version_id,
                event_code,
                actor_user_id,
                actor_email,
                reason,
                previous_state,
                new_state,
                event_metadata
            )
            VALUES (
                previous_id,
                'ROLLBACK_089_MODULE001A_ROLE_CATALOG_RESTORED',
                NULL,
                'rollback-089@pulse.local',
                'Restore the scoped role-policy version that preceded migration 089.',
                jsonb_build_object('policyVersionId', replacement_id),
                jsonb_build_object('policyVersionId', previous_id),
                jsonb_build_object('immutableAudit', TRUE)
            );
        END IF;
    ELSIF current_published_id IS DISTINCT FROM previous_id THEN
        RAISE EXCEPTION
            'Rollback 089 refused: a newer scoped role-policy version (%) is published.',
            current_published_id;
    END IF;
END;
$projectpulse089_rollback_policy$;

DO $projectpulse089_rollback_permissions$
BEGIN
    IF to_regclass('public.module_catalog_reconciliation_089_permission_grants') IS NULL THEN
        RETURN;
    END IF;

    DELETE FROM app_role_permissions relationship
    USING module_catalog_reconciliation_089_permission_grants introduced
    WHERE relationship.app_role_id = introduced.app_role_id
      AND relationship.app_permission_id = introduced.app_permission_id;
END;
$projectpulse089_rollback_permissions$;

DO $projectpulse089_rollback_modules$
BEGIN
    IF to_regclass('public.module_catalog_reconciliation_089_modules') IS NULL THEN
        RETURN;
    END IF;

    UPDATE scoped_role_policy_modules module
    SET module_name = evidence.previous_module_name,
        route_scope = evidence.previous_route_scope,
        current_state = evidence.previous_current_state,
        permission_notes = evidence.previous_permission_notes,
        source_url = evidence.previous_source_url,
        is_active = evidence.previous_is_active
    FROM module_catalog_reconciliation_089_modules evidence
    WHERE evidence.was_present = TRUE
      AND module.module_code = evidence.module_code;

    UPDATE scoped_role_policy_modules module
    SET current_state = 'Rolled back',
        permission_notes = 'Migration 089 registration rolled back. The inactive row is retained because immutable policy history may reference it.',
        is_active = FALSE
    FROM module_catalog_reconciliation_089_modules evidence
    WHERE evidence.was_present = FALSE
      AND module.module_code = evidence.module_code;
END;
$projectpulse089_rollback_modules$;

DELETE FROM schema_migrations
WHERE migration_id = '089_module_catalog_role_administration_reconciliation';

DROP TABLE IF EXISTS module_catalog_reconciliation_089_permission_grants;
DROP TABLE IF EXISTS module_catalog_reconciliation_089_policy_versions;
DROP TABLE IF EXISTS module_catalog_reconciliation_089_modules;

COMMIT;
