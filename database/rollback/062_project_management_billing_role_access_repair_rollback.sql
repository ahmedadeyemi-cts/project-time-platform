-- Rollback ProjectPulse migration 062.
-- Restores only relationships and scope values changed by migration 062.

BEGIN;

-- Restore Billing Module 008 permission relationships removed by migration 062.
INSERT INTO app_role_permissions (
    app_role_id,
    app_permission_id,
    created_at
)
SELECT
    role_id,
    permission_id,
    NOW()
FROM role_access_repair_062_permission_removals
ON CONFLICT (app_role_id, app_permission_id) DO NOTHING;

-- Remove only Project Management relationships added by migration 062.
DELETE FROM app_role_permissions relationship
USING role_access_repair_062_permission_grants change
WHERE relationship.app_role_id = change.role_id
  AND relationship.app_permission_id = change.permission_id;

DO $restore_scope$
BEGIN
    IF to_regclass('public.projectpulse_role_scope_rules') IS NOT NULL
       AND to_regclass('public.role_access_repair_062_scope_changes') IS NOT NULL THEN
        UPDATE projectpulse_role_scope_rules scope
        SET can_view_assigned_self = change.previous_can_view_assigned_self,
            can_approve_time = change.previous_can_approve_time,
            notes = change.previous_notes,
            updated_at = NOW()
        FROM role_access_repair_062_scope_changes change
        WHERE upper(scope.role_code) = upper(change.role_code);
    END IF;
END;
$restore_scope$;

-- Published grants are immutable. If the migration-created policy is still the
-- published version, publish a new restoration version copied from the previous
-- policy rather than mutating retired history.
DO $restore_policy$
DECLARE
    previous_id UUID;
    replacement_id UUID;
    current_published UUID;
    restore_id UUID;
    restore_number INTEGER;
BEGIN
    IF to_regclass('public.role_access_repair_062_policy_versions') IS NULL
       OR to_regclass('public.scoped_role_policy_versions') IS NULL
       OR to_regclass('public.scoped_role_policy_grants') IS NULL THEN
        RETURN;
    END IF;

    SELECT previous_policy_version_id, replacement_policy_version_id
    INTO previous_id, replacement_id
    FROM role_access_repair_062_policy_versions
    WHERE singleton_key = TRUE;

    IF previous_id IS NULL OR replacement_id IS NULL THEN
        RETURN;
    END IF;

    SELECT policy_version_id
    INTO current_published
    FROM scoped_role_policy_versions
    WHERE policy_status = 'PUBLISHED'
    ORDER BY version_number DESC
    LIMIT 1;

    IF current_published IS DISTINCT FROM replacement_id THEN
        -- A later administrator-published policy exists. Do not overwrite it.
        RETURN;
    END IF;

    SELECT COALESCE(MAX(version_number), 0) + 1
    INTO restore_number
    FROM scoped_role_policy_versions;
    restore_id := gen_random_uuid();

    INSERT INTO scoped_role_policy_versions (
        policy_version_id,
        version_number,
        policy_name,
        policy_status,
        source_name,
        source_sha256,
        policy_notes,
        created_by_user_id,
        published_by_user_id,
        restored_from_policy_version_id,
        created_at
    )
    SELECT
        restore_id,
        restore_number,
        policy_name || ' · rollback 062 restoration',
        'DRAFT',
        'rollback_062_project_management_billing_role_access_repair',
        encode(digest('rollback-062:' || restore_number::text, 'sha256'), 'hex'),
        concat_ws(' ', NULLIF(policy_notes, ''), 'Restored as a new immutable policy version by rollback 062.'),
        created_by_user_id,
        published_by_user_id,
        previous_id,
        NOW()
    FROM scoped_role_policy_versions
    WHERE policy_version_id = previous_id;

    INSERT INTO scoped_role_policy_grants (
        policy_version_id,
        role_code,
        module_code,
        action_code,
        scope_code,
        grant_effect,
        conditions,
        delegated_authority,
        reason_required,
        audit_required,
        source_designation,
        source_notes,
        is_active,
        created_at
    )
    SELECT
        restore_id,
        role_code,
        module_code,
        action_code,
        scope_code,
        grant_effect,
        conditions,
        delegated_authority,
        reason_required,
        audit_required,
        source_designation,
        source_notes,
        is_active,
        NOW()
    FROM scoped_role_policy_grants
    WHERE policy_version_id = previous_id;

    UPDATE scoped_role_policy_versions
    SET policy_status = 'RETIRED',
        retired_at = NOW()
    WHERE policy_version_id = replacement_id;

    UPDATE scoped_role_policy_versions
    SET policy_status = 'PUBLISHED',
        published_at = NOW()
    WHERE policy_version_id = restore_id;
END;
$restore_policy$;

DELETE FROM schema_migrations
WHERE migration_id = '062_project_management_billing_role_access_repair';

DROP TRIGGER IF EXISTS trg_role_access_repair_062_grants_immutable
    ON role_access_repair_062_permission_grants;
DROP TRIGGER IF EXISTS trg_role_access_repair_062_removals_immutable
    ON role_access_repair_062_permission_removals;
DROP TRIGGER IF EXISTS trg_role_access_repair_062_scopes_immutable
    ON role_access_repair_062_scope_changes;
DROP TRIGGER IF EXISTS trg_role_access_repair_062_policy_immutable
    ON role_access_repair_062_policy_versions;

DROP TABLE IF EXISTS role_access_repair_062_permission_grants;
DROP TABLE IF EXISTS role_access_repair_062_permission_removals;
DROP TABLE IF EXISTS role_access_repair_062_scope_changes;
DROP TABLE IF EXISTS role_access_repair_062_policy_versions;
DROP FUNCTION IF EXISTS projectpulse_062_block_evidence_mutation();

COMMIT;
