-- Guarded rollback for migration 046.
BEGIN;

DO $projectpulse046_rollback_guard$
BEGIN
    IF to_regclass('public.microsoft_integration_sso_client_secrets') IS NOT NULL
       AND EXISTS (SELECT 1 FROM microsoft_integration_sso_client_secrets) THEN
        RAISE EXCEPTION 'Rollback blocked: Microsoft SSO App Registration secret metadata exists.';
    END IF;
END;
$projectpulse046_rollback_guard$;

DROP TABLE IF EXISTS microsoft_integration_sso_client_secrets;

DO $projectpulse046_remove_registration$
BEGIN
    IF to_regclass('public.schema_migrations') IS NOT NULL THEN
        DELETE FROM schema_migrations
        WHERE migration_id = '046_microsoft_sso_connection_profiles';
    END IF;
END;
$projectpulse046_remove_registration$;

COMMIT;
