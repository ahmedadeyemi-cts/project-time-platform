-- Pulse migration 098
-- Register Module 001B - Time Reallocation & Corrections in the persistent
-- Role Administration module catalog without rewriting historical migration 089.
--
-- Authorization remains fail-closed in the application: only
-- PROJECT_TEAM_COORDINATOR and SUPER_ADMINISTRATOR may use Module 001B.

BEGIN;

DO $projectpulse098_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.scoped_role_policy_modules') IS NULL THEN
        RAISE EXCEPTION 'Migration 098 requires schema_migrations and the scoped role-policy module catalog.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM schema_migrations
        WHERE migration_id = '089_module_catalog_role_administration_reconciliation'
    ) THEN
        RAISE EXCEPTION 'Migration 098 requires migration 089 first.';
    END IF;
END;
$projectpulse098_prerequisites$;

INSERT INTO scoped_role_policy_modules (
    module_code,
    module_name,
    route_scope,
    current_state,
    permission_notes,
    source_url,
    is_active
)
VALUES (
    '001B',
    'Time Reallocation & Corrections',
    'time-reallocation',
    'Installed',
    'Time Management · Project Team Coordinator and Super Administrator only. Allocation-only correction preserves worker, work date, worked hours, and submission/approval state; no unsubmit, Draft transition, worker resubmission, Manager approval, or Project Manager approval is required.',
    'src/frontend/project-time-web/src/module-availability-registry.js',
    TRUE
)
ON CONFLICT (module_code) DO UPDATE
SET module_name = EXCLUDED.module_name,
    route_scope = EXCLUDED.route_scope,
    current_state = EXCLUDED.current_state,
    permission_notes = EXCLUDED.permission_notes,
    source_url = EXCLUDED.source_url,
    is_active = TRUE;

DO $projectpulse098_assertions$
DECLARE
    catalog_mismatches INTEGER;
BEGIN
    SELECT COUNT(*)
    INTO catalog_mismatches
    FROM scoped_role_policy_modules
    WHERE upper(module_code) = '001B'
      AND module_name = 'Time Reallocation & Corrections'
      AND route_scope = 'time-reallocation'
      AND current_state = 'Installed'
      AND is_active = TRUE;

    IF catalog_mismatches <> 1 THEN
        RAISE EXCEPTION 'Migration 098 invariant failed: Module 001B Role Administration catalog registration is missing or inconsistent.';
    END IF;
END;
$projectpulse098_assertions$;

INSERT INTO schema_migrations (
    migration_id,
    description,
    applied_at
)
VALUES (
    '098_module001b_role_catalog_registration',
    'Register Module 001B Time Reallocation & Corrections in Role Administration catalog',
    NOW()
)
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;