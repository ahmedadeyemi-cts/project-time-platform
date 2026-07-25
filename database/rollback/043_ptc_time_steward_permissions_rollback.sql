-- Guarded rollback for migration 043.
-- Fails closed after operational evidence or a later policy version exists.
BEGIN;

DO $projectpulse043_rollback_guard$
DECLARE
    v_migration_version INTEGER;
    v_migration_status TEXT;
BEGIN
    IF to_regclass('public.scoped_time_management_events') IS NOT NULL
       AND EXISTS (SELECT 1 FROM scoped_time_management_events)
    THEN
        RAISE EXCEPTION
            'Rollback 043 is blocked because PTC time-management audit evidence exists.';
    END IF;

    SELECT version_number, policy_status
    INTO v_migration_version, v_migration_status
    FROM scoped_role_policy_versions
    WHERE source_name = '043_ptc_time_steward_permissions';

    IF v_migration_version IS NULL THEN
        RAISE EXCEPTION 'Rollback 043 cannot find its immutable policy version.';
    END IF;

    IF v_migration_status <> 'PUBLISHED' THEN
        RAISE EXCEPTION
            'Rollback 043 is blocked because its policy version is not the currently published version.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM scoped_role_policy_versions
        WHERE version_number > v_migration_version
    ) THEN
        RAISE EXCEPTION
            'Rollback 043 is blocked because later policy versions exist.';
    END IF;
END;
$projectpulse043_rollback_guard$;

DO $projectpulse043_restore_policy$
DECLARE
    v_migration_policy UUID;
    v_migration_version INTEGER;
    v_previous_policy UUID;
BEGIN
    SELECT policy_version_id, version_number
    INTO v_migration_policy, v_migration_version
    FROM scoped_role_policy_versions
    WHERE source_name = '043_ptc_time_steward_permissions'
    FOR UPDATE;

    SELECT policy_version_id
    INTO v_previous_policy
    FROM scoped_role_policy_versions
    WHERE version_number < v_migration_version
    ORDER BY version_number DESC
    LIMIT 1
    FOR UPDATE;

    IF v_previous_policy IS NULL THEN
        RAISE EXCEPTION 'Rollback 043 cannot find the previous policy version.';
    END IF;

    UPDATE scoped_role_policy_versions
    SET policy_status = 'RETIRED', retired_at = NOW()
    WHERE policy_version_id = v_migration_policy;

    UPDATE scoped_role_policy_versions
    SET policy_status = 'PUBLISHED', published_at = NOW(), retired_at = NULL
    WHERE policy_version_id = v_previous_policy;

    DROP TRIGGER IF EXISTS trg_projectpulse040_published_grants_immutable
    ON scoped_role_policy_grants;

    DELETE FROM scoped_role_policy_grants
    WHERE policy_version_id = v_migration_policy;

    CREATE TRIGGER trg_projectpulse040_published_grants_immutable
    BEFORE UPDATE OR DELETE ON scoped_role_policy_grants
    FOR EACH ROW EXECUTE FUNCTION projectpulse040_block_published_grant_mutation();

    DELETE FROM scoped_role_policy_versions
    WHERE policy_version_id = v_migration_policy;
END;
$projectpulse043_restore_policy$;

DROP TRIGGER IF EXISTS trg_projectpulse043_time_management_audit_immutable
ON scoped_time_management_events;
DROP TABLE IF EXISTS scoped_time_management_events;

ALTER TABLE module001_timesheet_entry_associations
    DROP CONSTRAINT IF EXISTS chk_module001_association_source;
ALTER TABLE module001_timesheet_entry_associations
    ADD CONSTRAINT chk_module001_association_source CHECK (
        association_source IN ('EXISTING_ENTRY','WORK_QUEUE','TIMER','CALENDAR')
    );

DELETE FROM scoped_role_policy_actions action_row
WHERE action_row.action_code IN (
    'TIME_VIEW_ON_BEHALF',
    'TIME_UNSUBMIT',
    'TIME_DELETE_ON_BEHALF',
    'TIME_TASK_CREATE',
    'TIME_TASK_ASSIGN'
)
AND NOT EXISTS (
    SELECT 1
    FROM scoped_role_policy_grants grant_row
    WHERE grant_row.action_code = action_row.action_code
);

DELETE FROM schema_migrations
WHERE migration_id = '043_ptc_time_steward_permissions';

COMMIT;
