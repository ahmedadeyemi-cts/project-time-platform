-- Non-destructive rollback for migration 047.
-- The carried-over Module 010 and Module 067 configuration remains in Module 065
-- so rollback cannot break active Microsoft, identity, directory, or mail services.
BEGIN;

DO $projectpulse047_rollback$
BEGIN
    IF to_regclass('public.schema_migrations') IS NOT NULL THEN
        DELETE FROM schema_migrations
        WHERE migration_id = '047_microsoft_integration_connection_carryover';
    END IF;

    IF to_regclass('public.scoped_role_policy_modules') IS NOT NULL THEN
        UPDATE scoped_role_policy_modules
        SET module_name = 'Microsoft Integration',
            current_state = 'Installed consolidated Microsoft integration',
            permission_notes = 'Module 065 retains all carried-over Microsoft connection metadata and legacy Module 067 mail configuration after rollback.'
        WHERE module_code = '065';
    END IF;
END;
$projectpulse047_rollback$;

COMMIT;
