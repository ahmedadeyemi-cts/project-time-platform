-- Pulse migration 100
-- Reconcile Module 001B with the authoritative Module Management catalog so
-- descriptive ownership can be read and changed through the governed API.
-- Ownership remains accountability metadata and never grants module access.

BEGIN;

DO $projectpulse100_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.scoped_role_policy_modules') IS NULL
       OR to_regclass('public.scoped_role_policy_audit_events') IS NULL
       OR to_regclass('public.app_users') IS NULL THEN
        RAISE EXCEPTION 'Migration 100 requires the schema, module catalog, audit, and identity foundations.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM schema_migrations
        WHERE migration_id = '098_module_management_owner_storage_reconciliation'
    ) THEN
        RAISE EXCEPTION 'Migration 100 requires reconciled Module Management owner storage from migration 098.';
    END IF;
END;
$projectpulse100_prerequisites$;

CREATE TABLE IF NOT EXISTS module_catalog_reconciliation_100_module001b_evidence (
    module_code TEXT PRIMARY KEY,
    was_present BOOLEAN NOT NULL,
    previous_module_name TEXT NULL,
    previous_route_scope TEXT NULL,
    previous_current_state TEXT NULL,
    previous_permission_notes TEXT NULL,
    previous_source_url TEXT NULL,
    previous_is_active BOOLEAN NULL,
    previous_owner_user_id UUID NULL,
    previous_owner_revision_number INTEGER NULL,
    previous_owner_updated_at TIMESTAMPTZ NULL,
    previous_owner_updated_by_user_id UUID NULL,
    reconciled_owner_user_id UUID NULL,
    reconciled_owner_revision_number INTEGER NULL,
    reconciled_owner_updated_at TIMESTAMPTZ NULL,
    reconciled_owner_updated_by_user_id UUID NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO module_catalog_reconciliation_100_module001b_evidence (
    module_code,
    was_present,
    previous_module_name,
    previous_route_scope,
    previous_current_state,
    previous_permission_notes,
    previous_source_url,
    previous_is_active,
    previous_owner_user_id,
    previous_owner_revision_number,
    previous_owner_updated_at,
    previous_owner_updated_by_user_id
)
SELECT
    '001B',
    existing.module_code IS NOT NULL,
    existing.module_name,
    existing.route_scope,
    existing.current_state,
    existing.permission_notes,
    existing.source_url,
    existing.is_active,
    existing.owner_user_id,
    existing.owner_revision_number,
    existing.owner_updated_at,
    existing.owner_updated_by_user_id
FROM (SELECT 1) singleton
LEFT JOIN scoped_role_policy_modules existing
  ON existing.module_code = '001B'
ON CONFLICT (module_code) DO NOTHING;

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
    'Canonical Pulse module catalog entry · Time Management. Reconciled by migration 100.',
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

DO $projectpulse100_assign_existing_catalog_owner$
DECLARE
    default_owner_user_id UUID;
BEGIN
    SELECT module.owner_user_id
    INTO default_owner_user_id
    FROM scoped_role_policy_modules module
    JOIN app_users owner_user
      ON owner_user.user_id = module.owner_user_id
     AND owner_user.is_active = TRUE
    WHERE module.is_active = TRUE
      AND module.module_code <> '001B'
      AND module.owner_user_id IS NOT NULL
    GROUP BY module.owner_user_id
    ORDER BY COUNT(*) DESC, module.owner_user_id
    LIMIT 1;

    IF default_owner_user_id IS NOT NULL THEN
        UPDATE scoped_role_policy_modules
        SET owner_user_id = default_owner_user_id,
            owner_revision_number = 1,
            owner_updated_at = NOW(),
            owner_updated_by_user_id = default_owner_user_id
        WHERE module_code = '001B'
          AND owner_user_id IS NULL
          AND owner_revision_number = 0;
    END IF;
END;
$projectpulse100_assign_existing_catalog_owner$;

UPDATE module_catalog_reconciliation_100_module001b_evidence evidence
SET reconciled_owner_user_id = module.owner_user_id,
    reconciled_owner_revision_number = module.owner_revision_number,
    reconciled_owner_updated_at = module.owner_updated_at,
    reconciled_owner_updated_by_user_id = module.owner_updated_by_user_id
FROM scoped_role_policy_modules module
WHERE evidence.module_code = '001B'
  AND module.module_code = '001B'
  AND evidence.reconciled_owner_revision_number IS NULL;

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
SELECT
    NULL,
    'MODULE_001B_CATALOG_RECONCILED',
    NULL,
    '',
    'Migration 100 reconciled Module 001B with the authoritative Module Management catalog.',
    jsonb_build_object(
        'wasPresent', evidence.was_present,
        'moduleNumber', evidence.module_code,
        'ownerUserId', evidence.previous_owner_user_id,
        'revision', evidence.previous_owner_revision_number
    ),
    jsonb_build_object(
        'moduleNumber', module.module_code,
        'moduleName', module.module_name,
        'routeScope', module.route_scope,
        'ownerUserId', module.owner_user_id,
        'revision', module.owner_revision_number
    ),
    jsonb_build_object(
        'migration', '100_module001b_catalog_ownership_reconciliation',
        'immutableAudit', TRUE,
        'ownershipDoesNotGrantAccess', TRUE
    )
FROM module_catalog_reconciliation_100_module001b_evidence evidence
JOIN scoped_role_policy_modules module
  ON module.module_code = evidence.module_code
WHERE NOT EXISTS (
    SELECT 1
    FROM scoped_role_policy_audit_events audit
    WHERE audit.event_code = 'MODULE_001B_CATALOG_RECONCILED'
      AND audit.event_metadata ->> 'migration' = '100_module001b_catalog_ownership_reconciliation'
);

DO $projectpulse100_verify$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM scoped_role_policy_modules
        WHERE module_code = '001B'
          AND module_name = 'Time Reallocation & Corrections'
          AND route_scope = 'time-reallocation'
          AND current_state = 'Installed'
          AND is_active = TRUE
          AND owner_revision_number IS NOT NULL
    ) THEN
        RAISE EXCEPTION 'Migration 100 did not reconcile the canonical active Module 001B owner-catalog row.';
    END IF;
END;
$projectpulse100_verify$;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '100_module001b_catalog_ownership_reconciliation',
    'Reconcile Module 001B with the authoritative Module Management owner catalog',
    NOW()
)
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
