-- ProjectPulse migration 061
-- Permanent Super Administrator authority and legacy Administrator reconciliation.
--
-- This migration does not store credentials, call an external provider, change
-- Azure resources, or widen any non-administrative role. It records only the
-- administrator assignments and permission relationships that it creates so a
-- reviewed rollback can remove this migration's changes without disturbing
-- pre-existing authority.

BEGIN;

DO $prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.app_users') IS NULL
       OR to_regclass('public.app_roles') IS NULL
       OR to_regclass('public.app_permissions') IS NULL
       OR to_regclass('public.app_role_permissions') IS NULL
       OR to_regclass('public.app_user_role_assignments') IS NULL THEN
        RAISE EXCEPTION 'Migration 061 requires the ProjectPulse RBAC foundation.';
    END IF;
END;
$prerequisites$;

INSERT INTO app_roles (
    role_code,
    role_name,
    role_description,
    is_system_role,
    is_active,
    display_order,
    created_at,
    updated_at
)
VALUES
    (
        'SUPER_ADMINISTRATOR',
        'Super Administrator',
        'Permanent organization-wide Full Control across every active ProjectPulse module. This authority is valid only in the administrator''s own authenticated session and never transfers into View-As.',
        TRUE,
        TRUE,
        120,
        NOW(),
        NOW()
    ),
    (
        'ADMINISTRATOR',
        'Administrator',
        'Legacy compatibility alias for Super Administrator. New assignments must use SUPER_ADMINISTRATOR.',
        TRUE,
        TRUE,
        121,
        NOW(),
        NOW()
    )
ON CONFLICT (role_code) DO UPDATE
SET role_name = EXCLUDED.role_name,
    role_description = EXCLUDED.role_description,
    is_system_role = TRUE,
    is_active = TRUE,
    display_order = EXCLUDED.display_order,
    updated_at = NOW();

CREATE TABLE IF NOT EXISTS role_access_repair_061_assignment_changes (
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    target_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE RESTRICT,
    previous_assignment_existed BOOLEAN NOT NULL,
    previous_is_active BOOLEAN NULL,
    previous_assignment_reason TEXT NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (user_id, target_role_id)
);

CREATE TABLE IF NOT EXISTS role_access_repair_061_permission_changes (
    role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE RESTRICT,
    permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    role_code VARCHAR(75) NOT NULL,
    permission_code VARCHAR(100) NOT NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (role_id, permission_id)
);

CREATE OR REPLACE FUNCTION projectpulse_061_block_evidence_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse_061_immutable$
BEGIN
    RAISE EXCEPTION 'ProjectPulse migration 061 evidence is immutable.';
END;
$projectpulse_061_immutable$;

DROP TRIGGER IF EXISTS trg_role_access_repair_061_assignments_immutable
    ON role_access_repair_061_assignment_changes;
CREATE TRIGGER trg_role_access_repair_061_assignments_immutable
BEFORE UPDATE OR DELETE ON role_access_repair_061_assignment_changes
FOR EACH ROW EXECUTE FUNCTION projectpulse_061_block_evidence_mutation();

DROP TRIGGER IF EXISTS trg_role_access_repair_061_permissions_immutable
    ON role_access_repair_061_permission_changes;
CREATE TRIGGER trg_role_access_repair_061_permissions_immutable
BEFORE UPDATE OR DELETE ON role_access_repair_061_permission_changes
FOR EACH ROW EXECUTE FUNCTION projectpulse_061_block_evidence_mutation();

-- Reconcile every active legacy ADMINISTRATOR assignment to the canonical
-- SUPER_ADMINISTRATOR role. Existing canonical assignments are not modified.
WITH canonical_role AS (
    SELECT app_role_id
    FROM app_roles
    WHERE upper(role_code) = 'SUPER_ADMINISTRATOR'
      AND is_active = TRUE
    LIMIT 1
),
legacy_administrators AS (
    SELECT DISTINCT assignment.user_id
    FROM app_user_role_assignments assignment
    JOIN app_roles role
      ON role.app_role_id = assignment.app_role_id
     AND role.is_active = TRUE
    JOIN app_users app_user
      ON app_user.user_id = assignment.user_id
     AND app_user.is_active = TRUE
    WHERE assignment.is_active = TRUE
      AND upper(role.role_code) = 'ADMINISTRATOR'
),
candidates AS (
    SELECT
        legacy.user_id,
        canonical.app_role_id AS target_role_id,
        existing.app_user_role_assignment_id IS NOT NULL AS previous_assignment_existed,
        existing.is_active AS previous_is_active,
        existing.assignment_reason AS previous_assignment_reason
    FROM legacy_administrators legacy
    CROSS JOIN canonical_role canonical
    LEFT JOIN app_user_role_assignments existing
      ON existing.user_id = legacy.user_id
     AND existing.app_role_id = canonical.app_role_id
    WHERE existing.app_user_role_assignment_id IS NULL
       OR existing.is_active = FALSE
)
INSERT INTO role_access_repair_061_assignment_changes (
    user_id,
    target_role_id,
    previous_assignment_existed,
    previous_is_active,
    previous_assignment_reason
)
SELECT
    user_id,
    target_role_id,
    previous_assignment_existed,
    previous_is_active,
    previous_assignment_reason
FROM candidates
ON CONFLICT (user_id, target_role_id) DO NOTHING;

INSERT INTO app_user_role_assignments (
    user_id,
    app_role_id,
    assignment_reason,
    is_active,
    assigned_at,
    updated_at
)
SELECT
    change.user_id,
    change.target_role_id,
    'Migration 061 canonical Super Administrator reconciliation',
    TRUE,
    NOW(),
    NOW()
FROM role_access_repair_061_assignment_changes change
ON CONFLICT (user_id, app_role_id) DO UPDATE
SET is_active = TRUE,
    assignment_reason = EXCLUDED.assignment_reason,
    updated_at = NOW();

-- Super Administrator is an invariant rather than a collection of optional
-- grants. The source authorization layer enforces that invariant. These rows
-- keep legacy permission-driven modules consistent and make the matrix explicit.
WITH administrator_roles AS (
    SELECT app_role_id, upper(role_code) AS role_code
    FROM app_roles
    WHERE is_active = TRUE
      AND upper(role_code) IN ('SUPER_ADMINISTRATOR', 'ADMINISTRATOR')
),
candidates AS (
    SELECT
        role.app_role_id AS role_id,
        permission.app_permission_id AS permission_id,
        role.role_code,
        permission.permission_code
    FROM administrator_roles role
    CROSS JOIN app_permissions permission
    LEFT JOIN app_role_permissions existing
      ON existing.app_role_id = role.app_role_id
     AND existing.app_permission_id = permission.app_permission_id
    WHERE existing.app_role_permission_id IS NULL
)
INSERT INTO role_access_repair_061_permission_changes (
    role_id,
    permission_id,
    role_code,
    permission_code
)
SELECT role_id, permission_id, role_code, permission_code
FROM candidates
ON CONFLICT (role_id, permission_id) DO NOTHING;

INSERT INTO app_role_permissions (
    app_role_id,
    app_permission_id,
    created_at
)
SELECT
    change.role_id,
    change.permission_id,
    NOW()
FROM role_access_repair_061_permission_changes change
ON CONFLICT (app_role_id, app_permission_id) DO NOTHING;

DO $assert_invariants$
DECLARE
    active_super_administrators INTEGER;
    missing_permission_relationships INTEGER;
BEGIN
    SELECT COUNT(DISTINCT assignment.user_id)
    INTO active_super_administrators
    FROM app_user_role_assignments assignment
    JOIN app_roles role
      ON role.app_role_id = assignment.app_role_id
     AND role.is_active = TRUE
    JOIN app_users app_user
      ON app_user.user_id = assignment.user_id
     AND app_user.is_active = TRUE
    WHERE assignment.is_active = TRUE
      AND upper(role.role_code) = 'SUPER_ADMINISTRATOR';

    IF active_super_administrators < 1 THEN
        RAISE EXCEPTION 'Migration 061 invariant failed: at least one active Super Administrator assignment is required.';
    END IF;

    SELECT COUNT(*)
    INTO missing_permission_relationships
    FROM app_roles role
    CROSS JOIN app_permissions permission
    LEFT JOIN app_role_permissions relationship
      ON relationship.app_role_id = role.app_role_id
     AND relationship.app_permission_id = permission.app_permission_id
    WHERE role.is_active = TRUE
      AND upper(role.role_code) IN ('SUPER_ADMINISTRATOR', 'ADMINISTRATOR')
      AND relationship.app_role_permission_id IS NULL;

    IF missing_permission_relationships <> 0 THEN
        RAISE EXCEPTION 'Migration 061 invariant failed: % administrator permission relationships are missing.', missing_permission_relationships;
    END IF;
END;
$assert_invariants$;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '061_super_administrator_permanent_full_control',
    'Reconcile legacy Administrator assignments and explicitly grant every registered permission to permanent Super Administrator authority',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
