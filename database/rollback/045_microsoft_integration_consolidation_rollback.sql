-- Guarded rollback for migration 045.
BEGIN;

DO $projectpulse045_rollback_guard$
BEGIN
    IF to_regclass('public.microsoft_integration_client_secrets') IS NOT NULL
       AND EXISTS (SELECT 1 FROM microsoft_integration_client_secrets) THEN
        RAISE EXCEPTION 'Rollback blocked: Microsoft Integration client-secret metadata exists.';
    END IF;

    IF to_regclass('public.microsoft_integration_audit_events') IS NOT NULL
       AND EXISTS (SELECT 1 FROM microsoft_integration_audit_events) THEN
        RAISE EXCEPTION 'Rollback blocked: immutable Microsoft Integration audit evidence exists.';
    END IF;
END;
$projectpulse045_rollback_guard$;

DO $projectpulse045_restore_catalog$
BEGIN
    IF to_regclass('public.scoped_role_policy_modules') IS NOT NULL THEN
        UPDATE scoped_role_policy_modules
        SET module_name = 'Entra Secret Administration',
            route_scope = 'entra-secret-administration',
            current_state = 'Installed fail-closed',
            is_active = TRUE
        WHERE module_code = '065';

        UPDATE scoped_role_policy_modules
        SET module_name = 'Global Mail Configuration Center',
            route_scope = 'global-mail-configuration',
            current_state = 'Installed read-only',
            is_active = TRUE
        WHERE module_code = '067';
    END IF;
END;
$projectpulse045_restore_catalog$;

DROP TABLE IF EXISTS microsoft_integration_permission_aliases;
DROP TRIGGER IF EXISTS trg_projectpulse045_microsoft_integration_audit_immutable
ON microsoft_integration_audit_events;
DROP FUNCTION IF EXISTS projectpulse045_block_microsoft_integration_audit_mutation();
DROP TABLE IF EXISTS microsoft_integration_audit_events;
DROP TABLE IF EXISTS microsoft_integration_client_secrets;

DO $projectpulse045_remove_registration$
BEGIN
    IF to_regclass('public.schema_migrations') IS NOT NULL THEN
        DELETE FROM schema_migrations
        WHERE migration_id = '045_microsoft_integration_consolidation';
    END IF;
END;
$projectpulse045_remove_registration$;

COMMIT;
