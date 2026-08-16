-- Pulse migration 090
-- Add durable module-owner metadata, assign every active module to
-- ahmed.adeyemi@ussignal.com, and support Super Administrator owner changes.
-- Ownership is descriptive accountability metadata and never grants access.

BEGIN;

DO $projectpulse090_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.scoped_role_policy_modules') IS NULL
       OR to_regclass('public.scoped_role_policy_audit_events') IS NULL
       OR to_regclass('public.app_users') IS NULL
       OR to_regclass('public.auth_external_identity_links') IS NULL THEN
        RAISE EXCEPTION 'Migration 090 requires the identity and scoped role-policy foundations.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id = '089_module_catalog_role_administration_reconciliation'
    ) THEN
        RAISE EXCEPTION 'Migration 090 requires migration 089 so every built-in module is registered first.';
    END IF;
END;
$projectpulse090_prerequisites$;

ALTER TABLE scoped_role_policy_modules
    ADD COLUMN IF NOT EXISTS owner_user_id UUID NULL,
    ADD COLUMN IF NOT EXISTS owner_revision_number INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS owner_updated_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS owner_updated_by_user_id UUID NULL;

DO $projectpulse090_constraints$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_scoped_role_policy_modules_owner_user') THEN
        ALTER TABLE scoped_role_policy_modules
            ADD CONSTRAINT fk_scoped_role_policy_modules_owner_user
            FOREIGN KEY (owner_user_id) REFERENCES app_users(user_id) ON DELETE SET NULL;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_scoped_role_policy_modules_owner_updated_by') THEN
        ALTER TABLE scoped_role_policy_modules
            ADD CONSTRAINT fk_scoped_role_policy_modules_owner_updated_by
            FOREIGN KEY (owner_updated_by_user_id) REFERENCES app_users(user_id) ON DELETE SET NULL;
    END IF;
END;
$projectpulse090_constraints$;

CREATE INDEX IF NOT EXISTS ix_scoped_role_policy_modules_owner
ON scoped_role_policy_modules (owner_user_id, is_active, module_code);

CREATE TABLE IF NOT EXISTS module_catalog_ownership_090_evidence (
    module_code TEXT PRIMARY KEY,
    previous_owner_user_id UUID NULL,
    previous_owner_revision_number INTEGER NOT NULL,
    previous_owner_updated_at TIMESTAMPTZ NULL,
    previous_owner_updated_by_user_id UUID NULL,
    assigned_owner_user_id UUID NULL,
    assigned_owner_revision_number INTEGER NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO module_catalog_ownership_090_evidence (
    module_code,
    previous_owner_user_id,
    previous_owner_revision_number,
    previous_owner_updated_at,
    previous_owner_updated_by_user_id
)
SELECT
    module_code,
    owner_user_id,
    owner_revision_number,
    owner_updated_at,
    owner_updated_by_user_id
FROM scoped_role_policy_modules
WHERE is_active = TRUE
ON CONFLICT (module_code) DO NOTHING;

DO $projectpulse090_assign_owner$
DECLARE
    owner_id UUID;
BEGIN
    SELECT candidate.user_id
    INTO owner_id
    FROM (
        SELECT app_user.user_id, 0 AS priority
        FROM app_users app_user
        WHERE app_user.is_active = TRUE
          AND lower(app_user.email) = 'ahmed.adeyemi@ussignal.com'

        UNION ALL

        SELECT app_user.user_id, 1 AS priority
        FROM auth_external_identity_links external_identity
        JOIN app_users app_user
          ON app_user.user_id = external_identity.user_id
         AND app_user.is_active = TRUE
        WHERE external_identity.is_active = TRUE
          AND lower(COALESCE(NULLIF(external_identity.email, ''), external_identity.user_principal_name, '')) = 'ahmed.adeyemi@ussignal.com'
    ) candidate
    ORDER BY candidate.priority, candidate.user_id
    LIMIT 1;

    IF owner_id IS NULL THEN
        RAISE EXCEPTION 'Migration 090 could not resolve active identity ahmed.adeyemi@ussignal.com.';
    END IF;

    UPDATE scoped_role_policy_modules
    SET owner_user_id = owner_id,
        owner_revision_number = owner_revision_number + 1,
        owner_updated_at = NOW(),
        owner_updated_by_user_id = owner_id
    WHERE is_active = TRUE
      AND owner_user_id IS DISTINCT FROM owner_id;

    UPDATE scoped_role_policy_modules
    SET owner_updated_at = COALESCE(owner_updated_at, NOW()),
        owner_updated_by_user_id = COALESCE(owner_updated_by_user_id, owner_id)
    WHERE is_active = TRUE
      AND owner_user_id = owner_id;

    UPDATE module_catalog_ownership_090_evidence evidence
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
        'ahmed.adeyemi@ussignal.com',
        'Migration 090 assigned the requested default owner to every active Pulse module.',
        jsonb_build_object(
            'moduleNumber', evidence.module_code,
            'ownerUserId', evidence.previous_owner_user_id,
            'revision', evidence.previous_owner_revision_number
        ),
        jsonb_build_object(
            'moduleNumber', module.module_code,
            'ownerUserId', module.owner_user_id,
            'ownerEmail', 'ahmed.adeyemi@ussignal.com',
            'revision', module.owner_revision_number
        ),
        jsonb_build_object(
            'immutableAudit', TRUE,
            'ownershipDoesNotGrantAccess', TRUE,
            'migration', '090_module_management_table_and_ownership'
        )
    FROM module_catalog_ownership_090_evidence evidence
    JOIN scoped_role_policy_modules module
      ON module.module_code = evidence.module_code
    WHERE NOT EXISTS (
        SELECT 1
        FROM scoped_role_policy_audit_events audit
        WHERE audit.event_code = 'MODULE_OWNER_DEFAULT_ASSIGNED'
          AND audit.event_metadata ->> 'migration' = '090_module_management_table_and_ownership'
          AND audit.new_state ->> 'moduleNumber' = evidence.module_code
    );
END;
$projectpulse090_assign_owner$;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '090_module_management_table_and_ownership',
    'Add default Table presentation and durable Super Administrator-managed module ownership',
    NOW()
)
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
