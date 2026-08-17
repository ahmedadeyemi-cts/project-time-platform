-- Pulse migration 091
-- Repair durable Module Management owner storage and assign every active module
-- to the active Pulse identity associated with Ahmed.Adeyemi@ussignal.local.
-- Ownership is descriptive accountability metadata and never grants access.

BEGIN;

DO $projectpulse091_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.scoped_role_policy_modules') IS NULL
       OR to_regclass('public.scoped_role_policy_audit_events') IS NULL
       OR to_regclass('public.app_users') IS NULL THEN
        RAISE EXCEPTION 'Migration 091 requires the identity and scoped role-policy foundations.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM schema_migrations
        WHERE migration_id = '089_module_catalog_role_administration_reconciliation'
    ) THEN
        RAISE EXCEPTION 'Migration 091 requires migration 089 so every built-in module is registered first.';
    END IF;
END;
$projectpulse091_prerequisites$;

ALTER TABLE scoped_role_policy_modules
    ADD COLUMN IF NOT EXISTS owner_user_id UUID NULL,
    ADD COLUMN IF NOT EXISTS owner_revision_number INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS owner_updated_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS owner_updated_by_user_id UUID NULL;

DO $projectpulse091_constraints$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_scoped_role_policy_modules_owner_user'
          AND conrelid = 'public.scoped_role_policy_modules'::regclass
    ) THEN
        ALTER TABLE scoped_role_policy_modules
            ADD CONSTRAINT fk_scoped_role_policy_modules_owner_user
            FOREIGN KEY (owner_user_id) REFERENCES app_users(user_id) ON DELETE SET NULL;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_scoped_role_policy_modules_owner_updated_by'
          AND conrelid = 'public.scoped_role_policy_modules'::regclass
    ) THEN
        ALTER TABLE scoped_role_policy_modules
            ADD CONSTRAINT fk_scoped_role_policy_modules_owner_updated_by
            FOREIGN KEY (owner_updated_by_user_id) REFERENCES app_users(user_id) ON DELETE SET NULL;
    END IF;
END;
$projectpulse091_constraints$;

CREATE INDEX IF NOT EXISTS ix_scoped_role_policy_modules_owner
ON scoped_role_policy_modules (owner_user_id, is_active, module_code);

CREATE TABLE IF NOT EXISTS module_catalog_ownership_091_evidence (
    module_code TEXT PRIMARY KEY,
    previous_owner_user_id UUID NULL,
    previous_owner_revision_number INTEGER NOT NULL,
    previous_owner_updated_at TIMESTAMPTZ NULL,
    previous_owner_updated_by_user_id UUID NULL,
    assigned_owner_user_id UUID NULL,
    assigned_owner_revision_number INTEGER NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO module_catalog_ownership_091_evidence (
    module_code,
    previous_owner_user_id,
    previous_owner_revision_number,
    previous_owner_updated_at,
    previous_owner_updated_by_user_id
)
SELECT
    module_code,
    owner_user_id,
    COALESCE(owner_revision_number, 0),
    owner_updated_at,
    owner_updated_by_user_id
FROM scoped_role_policy_modules
WHERE is_active = TRUE
ON CONFLICT (module_code) DO NOTHING;

DO $projectpulse091_assign_owner$
DECLARE
    requested_email CONSTANT TEXT := lower('Ahmed.Adeyemi@ussignal.local');
    owner_id UUID;
    owner_display_name TEXT;
BEGIN
    SELECT
        app_user.user_id,
        COALESCE(NULLIF(app_user.display_name, ''), NULLIF(app_user.email, ''), 'Pulse module owner')
    INTO owner_id, owner_display_name
    FROM app_users app_user
    WHERE app_user.is_active = TRUE
      AND lower(app_user.email) = requested_email
    ORDER BY app_user.user_id
    LIMIT 1;

    IF owner_id IS NULL AND to_regclass('public.auth_external_identity_links') IS NOT NULL THEN
        SELECT
            app_user.user_id,
            COALESCE(NULLIF(app_user.display_name, ''), NULLIF(app_user.email, ''), 'Pulse module owner')
        INTO owner_id, owner_display_name
        FROM auth_external_identity_links external_identity
        JOIN app_users app_user
          ON app_user.user_id = external_identity.user_id
         AND app_user.is_active = TRUE
        WHERE external_identity.is_active = TRUE
          AND lower(COALESCE(NULLIF(external_identity.email, ''), external_identity.user_principal_name, '')) = requested_email
        ORDER BY app_user.user_id
        LIMIT 1;
    END IF;

    IF owner_id IS NULL THEN
        RAISE EXCEPTION 'Migration 091 could not resolve the active Pulse identity associated with %.', requested_email;
    END IF;

    UPDATE scoped_role_policy_modules
    SET owner_user_id = owner_id,
        owner_revision_number = COALESCE(owner_revision_number, 0) + 1,
        owner_updated_at = NOW(),
        owner_updated_by_user_id = owner_id
    WHERE is_active = TRUE
      AND owner_user_id IS DISTINCT FROM owner_id;

    UPDATE scoped_role_policy_modules
    SET owner_updated_at = COALESCE(owner_updated_at, NOW()),
        owner_updated_by_user_id = COALESCE(owner_updated_by_user_id, owner_id)
    WHERE is_active = TRUE
      AND owner_user_id = owner_id;

    UPDATE module_catalog_ownership_091_evidence evidence
    SET assigned_owner_user_id = module.owner_user_id,
        assigned_owner_revision_number = module.owner_revision_number
    FROM scoped_role_policy_modules module
    WHERE module.module_code = evidence.module_code;

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
        'MODULE_OWNER_DEFAULT_ASSIGNED',
        owner_id,
        requested_email,
        'Migration 091 repaired Module Management ownership storage and assigned the requested default owner.',
        jsonb_build_object(
            'moduleNumber', evidence.module_code,
            'ownerUserId', evidence.previous_owner_user_id,
            'revision', evidence.previous_owner_revision_number
        ),
        jsonb_build_object(
            'moduleNumber', module.module_code,
            'ownerUserId', module.owner_user_id,
            'ownerDisplayName', owner_display_name,
            'ownerEmail', requested_email,
            'revision', module.owner_revision_number
        ),
        jsonb_build_object(
            'immutableAudit', TRUE,
            'ownershipDoesNotGrantAccess', TRUE,
            'migration', '091_module_management_owner_storage_repair'
        )
    FROM module_catalog_ownership_091_evidence evidence
    JOIN scoped_role_policy_modules module
      ON module.module_code = evidence.module_code
    WHERE NOT EXISTS (
        SELECT 1
        FROM scoped_role_policy_audit_events audit
        WHERE audit.event_code = 'MODULE_OWNER_DEFAULT_ASSIGNED'
          AND audit.event_metadata ->> 'migration' = '091_module_management_owner_storage_repair'
          AND audit.new_state ->> 'moduleNumber' = evidence.module_code
    );
END;
$projectpulse091_assign_owner$;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '091_module_management_owner_storage_repair',
    'Repair durable module ownership storage and assign all active modules to Ahmed.Adeyemi@ussignal.local',
    NOW()
)
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
