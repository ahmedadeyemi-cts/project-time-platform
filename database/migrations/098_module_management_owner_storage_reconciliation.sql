-- Pulse migration 098
-- Reconcile durable Module Management owner storage without replaying historical
-- environment-specific ownership assignments. Ownership is descriptive
-- accountability metadata only and never grants access.

BEGIN;

DO $projectpulse098_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.scoped_role_policy_modules') IS NULL
       OR to_regclass('public.app_users') IS NULL THEN
        RAISE EXCEPTION 'Migration 098 requires the schema migration, module catalog, and application identity foundations.';
    END IF;
END;
$projectpulse098_prerequisites$;

ALTER TABLE scoped_role_policy_modules
    ADD COLUMN IF NOT EXISTS owner_user_id UUID NULL,
    ADD COLUMN IF NOT EXISTS owner_revision_number INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS owner_updated_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS owner_updated_by_user_id UUID NULL;

-- ADD COLUMN IF NOT EXISTS does not reconcile attributes of a pre-existing
-- column. Normalize the revision contract explicitly so the live API can rely
-- on a non-null optimistic-concurrency value in every partially migrated Test
-- database.
UPDATE scoped_role_policy_modules
SET owner_revision_number = 0
WHERE owner_revision_number IS NULL;

ALTER TABLE scoped_role_policy_modules
    ALTER COLUMN owner_revision_number SET DEFAULT 0,
    ALTER COLUMN owner_revision_number SET NOT NULL;

-- A partially migrated database can contain owner identifiers written before
-- the foreign-key constraints were installed. Preserve the module row while
-- clearing only orphaned descriptive owner metadata before enforcing the
-- canonical identity relationship.
UPDATE scoped_role_policy_modules module
SET owner_user_id = NULL,
    owner_updated_at = NOW()
WHERE module.owner_user_id IS NOT NULL
  AND NOT EXISTS (
      SELECT 1
      FROM app_users app_user
      WHERE app_user.user_id = module.owner_user_id
  );

UPDATE scoped_role_policy_modules module
SET owner_updated_by_user_id = NULL
WHERE module.owner_updated_by_user_id IS NOT NULL
  AND NOT EXISTS (
      SELECT 1
      FROM app_users app_user
      WHERE app_user.user_id = module.owner_updated_by_user_id
  );

DO $projectpulse098_constraints$
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
$projectpulse098_constraints$;

CREATE INDEX IF NOT EXISTS ix_scoped_role_policy_modules_owner
    ON scoped_role_policy_modules (owner_user_id, is_active, module_code);

DO $projectpulse098_verify$
DECLARE
    missing_columns INTEGER;
BEGIN
    SELECT COUNT(*)
    INTO missing_columns
    FROM (VALUES
        ('owner_user_id'),
        ('owner_revision_number'),
        ('owner_updated_at'),
        ('owner_updated_by_user_id')
    ) required(column_name)
    WHERE NOT EXISTS (
        SELECT 1
        FROM information_schema.columns column_record
        WHERE column_record.table_schema = 'public'
          AND column_record.table_name = 'scoped_role_policy_modules'
          AND column_record.column_name = required.column_name
    );

    IF missing_columns <> 0 THEN
        RAISE EXCEPTION 'Migration 098 could not reconcile % Module Management owner column(s).', missing_columns;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_scoped_role_policy_modules_owner_user'
          AND conrelid = 'public.scoped_role_policy_modules'::regclass
    ) OR NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_scoped_role_policy_modules_owner_updated_by'
          AND conrelid = 'public.scoped_role_policy_modules'::regclass
    ) THEN
        RAISE EXCEPTION 'Migration 098 could not reconcile the Module Management owner foreign-key contract.';
    END IF;
END;
$projectpulse098_verify$;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '098_module_management_owner_storage_reconciliation',
    'Reconcile Module Management owner storage independently of historical environment-specific owner assignment',
    NOW()
)
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
