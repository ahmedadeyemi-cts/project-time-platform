-- Pulse migration 098 rollback
-- Remove only the Role Administration catalog registration owned by migration 098.
-- Refuse rollback if policy grants have subsequently been published for Module 001B.

BEGIN;

DO $projectpulse098_rollback_guard$
BEGIN
    IF to_regclass('public.scoped_role_policy_modules') IS NULL THEN
        RAISE EXCEPTION 'Rollback 098 requires the scoped role-policy module catalog.';
    END IF;

    IF to_regclass('public.scoped_role_policy_grants') IS NOT NULL
       AND EXISTS (
           SELECT 1
           FROM scoped_role_policy_grants
           WHERE upper(module_code) = '001B'
             AND is_active = TRUE
       ) THEN
        RAISE EXCEPTION 'Rollback 098 refused: active scoped role-policy grants exist for Module 001B.';
    END IF;
END;
$projectpulse098_rollback_guard$;

DELETE FROM scoped_role_policy_modules
WHERE upper(module_code) = '001B'
  AND route_scope = 'time-reallocation';

DELETE FROM schema_migrations
WHERE migration_id = '098_module001b_role_catalog_registration';

COMMIT;