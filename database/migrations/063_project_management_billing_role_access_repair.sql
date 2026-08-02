-- ProjectPulse migration 063
-- Correct Project Management self-service/project-approval authority and the
-- Billing exclusion from Module 008 Audit History.
--
-- The migration is idempotent, records only relationships it changes, preserves
-- Accounting access, and never grants administrator authority to a business role.

BEGIN;

DO $prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.app_roles') IS NULL
       OR to_regclass('public.app_permissions') IS NULL
       OR to_regclass('public.app_role_permissions') IS NULL THEN
        RAISE EXCEPTION 'Migration 063 requires the ProjectPulse RBAC foundation.';
    END IF;
END;
$prerequisites$;

CREATE TABLE IF NOT EXISTS role_access_repair_063_permission_grants (
    role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE RESTRICT,
    permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    role_code VARCHAR(75) NOT NULL,
    permission_code VARCHAR(100) NOT NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (role_id, permission_id)
);

CREATE TABLE IF NOT EXISTS role_access_repair_063_permission_removals (
    role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE RESTRICT,
    permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    role_code VARCHAR(75) NOT NULL,
    permission_code VARCHAR(100) NOT NULL,
    module_code VARCHAR(75) NOT NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (role_id, permission_id)
);

CREATE TABLE IF NOT EXISTS role_access_repair_063_scope_changes (
    role_code VARCHAR(75) PRIMARY KEY,
    previous_can_view_assigned_self BOOLEAN NULL,
    previous_can_approve_time BOOLEAN NULL,
    previous_notes TEXT NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS role_access_repair_063_policy_versions (
    singleton_key BOOLEAN PRIMARY KEY DEFAULT TRUE CHECK (singleton_key),
    previous_policy_version_id UUID NULL,
    replacement_policy_version_id UUID NULL,
    previous_version_number INTEGER NULL,
    replacement_version_number INTEGER NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE OR REPLACE FUNCTION projectpulse_063_block_evidence_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse_063_immutable$
BEGIN
    RAISE EXCEPTION 'ProjectPulse migration 063 evidence is immutable.';
END;
$projectpulse_063_immutable$;

DROP TRIGGER IF EXISTS trg_role_access_repair_063_grants_immutable
    ON role_access_repair_063_permission_grants;
CREATE TRIGGER trg_role_access_repair_063_grants_immutable
BEFORE UPDATE OR DELETE ON role_access_repair_063_permission_grants
FOR EACH ROW EXECUTE FUNCTION projectpulse_063_block_evidence_mutation();

DROP TRIGGER IF EXISTS trg_role_access_repair_063_removals_immutable
    ON role_access_repair_063_permission_removals;
CREATE TRIGGER trg_role_access_repair_063_removals_immutable
BEFORE UPDATE OR DELETE ON role_access_repair_063_permission_removals
FOR EACH ROW EXECUTE FUNCTION projectpulse_063_block_evidence_mutation();

DROP TRIGGER IF EXISTS trg_role_access_repair_063_scopes_immutable
    ON role_access_repair_063_scope_changes;
CREATE TRIGGER trg_role_access_repair_063_scopes_immutable
BEFORE UPDATE OR DELETE ON role_access_repair_063_scope_changes
FOR EACH ROW EXECUTE FUNCTION projectpulse_063_block_evidence_mutation();

DROP TRIGGER IF EXISTS trg_role_access_repair_063_policy_immutable
    ON role_access_repair_063_policy_versions;
CREATE TRIGGER trg_role_access_repair_063_policy_immutable
BEFORE UPDATE OR DELETE ON role_access_repair_063_policy_versions
FOR EACH ROW EXECUTE FUNCTION projectpulse_063_block_evidence_mutation();

INSERT INTO app_permissions (
    permission_code,
    permission_name,
    module_code,
    permission_description
)
VALUES
    (
        'VIEW_QUALIFICATIONS_069',
        'View Qualifications and Certifications',
        '069',
        'View qualification and certification records within the server-authorized role scope.'
    ),
    (
        'MANAGE_OWN_QUALIFICATIONS_069',
        'Manage Own Qualifications and Certifications',
        '069',
        'Create and update only the authenticated user''s own qualification and certification records. View-As never transfers this authority.'
    )
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

CREATE TEMP TABLE projectpulse_063_pm_permissions (
    permission_code VARCHAR(100) PRIMARY KEY
) ON COMMIT DROP;

INSERT INTO projectpulse_063_pm_permissions (permission_code)
VALUES
    ('VIEW_TIME_ENTRY'),
    ('EDIT_OWN_TIME'),
    ('SUBMIT_OWN_TIME'),
    ('VIEW_APPROVAL_INBOX'),
    ('APPROVE_TIME'),
    ('REJECT_TIME'),
    ('PROJECT_TIME_APPROVAL'),
    ('VIEW_HOLIDAYS'),
    ('VIEW_CALENDAR'),
    ('VIEW_EXPENSES'),
    ('MANAGE_EXPENSES'),
    ('VIEW_PROJECT_WORKSPACE'),
    ('VIEW_REPORTS'),
    ('VIEW_QUALIFICATIONS_069'),
    ('MANAGE_OWN_QUALIFICATIONS_069');

WITH candidates AS (
    SELECT
        role.app_role_id AS role_id,
        permission.app_permission_id AS permission_id,
        upper(role.role_code) AS role_code,
        permission.permission_code
    FROM app_roles role
    JOIN projectpulse_063_pm_permissions required
      ON TRUE
    JOIN app_permissions permission
      ON upper(permission.permission_code) = upper(required.permission_code)
    LEFT JOIN app_role_permissions relationship
      ON relationship.app_role_id = role.app_role_id
     AND relationship.app_permission_id = permission.app_permission_id
    WHERE role.is_active = TRUE
      AND upper(role.role_code) IN (
          'PROJECT_MANAGER',
          'PROJECT_MANAGEMENT',
          'PROJECT_MANAGEMENT_LEAD',
          'PROJECT_MANAGEMENT_TEAM_LEAD',
          'PM_TEAM_LEAD'
      )
      AND relationship.app_role_permission_id IS NULL
)
INSERT INTO role_access_repair_063_permission_grants (
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
    role_id,
    permission_id,
    NOW()
FROM role_access_repair_063_permission_grants
ON CONFLICT (app_role_id, app_permission_id) DO NOTHING;

-- Billing is operationally separate from Accounting. Remove only Module 008
-- relationships held by Billing aliases; Accounting is deliberately excluded.
WITH candidates AS (
    SELECT
        role.app_role_id AS role_id,
        permission.app_permission_id AS permission_id,
        upper(role.role_code) AS role_code,
        permission.permission_code,
        permission.module_code
    FROM app_roles role
    JOIN app_role_permissions relationship
      ON relationship.app_role_id = role.app_role_id
    JOIN app_permissions permission
      ON permission.app_permission_id = relationship.app_permission_id
    WHERE role.is_active = TRUE
      AND upper(role.role_code) IN ('BILLING', 'ACCOUNTING_BILLING', 'FINANCE')
      AND (
            upper(COALESCE(permission.module_code, '')) = '008'
         OR upper(permission.permission_code) IN (
                'VIEW_AUDIT_TRAIL',
                'VIEW_AUDIT_HISTORY',
                'VIEW_AUDIT_HISTORY_008'
            )
      )
)
INSERT INTO role_access_repair_063_permission_removals (
    role_id,
    permission_id,
    role_code,
    permission_code,
    module_code
)
SELECT role_id, permission_id, role_code, permission_code, module_code
FROM candidates
ON CONFLICT (role_id, permission_id) DO NOTHING;

DELETE FROM app_role_permissions relationship
USING role_access_repair_063_permission_removals removal
WHERE relationship.app_role_id = removal.role_id
  AND relationship.app_permission_id = removal.permission_id;

-- Preserve and update the canonical PM scope contract when that optional table
-- is installed. This does not widen project data beyond existing managed-project
-- endpoint checks.
DO $scope_repair$
BEGIN
    IF to_regclass('public.projectpulse_role_scope_rules') IS NOT NULL THEN
        INSERT INTO role_access_repair_063_scope_changes (
            role_code,
            previous_can_view_assigned_self,
            previous_can_approve_time,
            previous_notes
        )
        SELECT
            role_code,
            can_view_assigned_self,
            can_approve_time,
            notes
        FROM projectpulse_role_scope_rules
        WHERE upper(role_code) IN ('PROJECT_MANAGEMENT', 'PROJECT_MANAGEMENT_LEAD')
        ON CONFLICT (role_code) DO NOTHING;

        UPDATE projectpulse_role_scope_rules
        SET can_view_assigned_self = TRUE,
            can_approve_time = TRUE,
            notes = CASE
                WHEN position('Migration 063' IN COALESCE(notes, '')) > 0 THEN notes
                ELSE concat_ws(' ', NULLIF(notes, ''), 'Migration 063 confirms own time entry, assigned-project approval, holiday view, project expense entry, and own qualification self-service.')
            END,
            updated_at = NOW()
        WHERE upper(role_code) IN ('PROJECT_MANAGEMENT', 'PROJECT_MANAGEMENT_LEAD');
    END IF;
END;
$scope_repair$;

-- Published scoped-policy grants are immutable. When the current published
-- policy explicitly grants Billing access to Module 008, publish a byte-for-byte
-- successor excluding only those Billing/008 rows. All other grants are copied.
DO $policy_repair$
DECLARE
    previous_id UUID;
    previous_number INTEGER;
    replacement_id UUID;
    replacement_number INTEGER;
    billing_grant_count INTEGER;
BEGIN
    IF to_regclass('public.scoped_role_policy_versions') IS NULL
       OR to_regclass('public.scoped_role_policy_grants') IS NULL THEN
        RETURN;
    END IF;

    SELECT policy_version_id, version_number
    INTO previous_id, previous_number
    FROM scoped_role_policy_versions
    WHERE policy_status = 'PUBLISHED'
    ORDER BY version_number DESC
    LIMIT 1;

    IF previous_id IS NULL THEN
        RETURN;
    END IF;

    SELECT COUNT(*)
    INTO billing_grant_count
    FROM scoped_role_policy_grants
    WHERE policy_version_id = previous_id
      AND is_active = TRUE
      AND upper(role_code) IN ('BILLING', 'ACCOUNTING_BILLING', 'FINANCE')
      AND upper(module_code) = '008'
      AND upper(grant_effect) = 'GRANT';

    IF billing_grant_count = 0 THEN
        RETURN;
    END IF;

    SELECT previous_policy_version_id, replacement_policy_version_id
    INTO previous_id, replacement_id
    FROM role_access_repair_063_policy_versions
    WHERE singleton_key = TRUE;

    IF replacement_id IS NOT NULL THEN
        RETURN;
    END IF;

    SELECT policy_version_id, version_number
    INTO previous_id, previous_number
    FROM scoped_role_policy_versions
    WHERE policy_status = 'PUBLISHED'
    ORDER BY version_number DESC
    LIMIT 1;

    SELECT COALESCE(MAX(version_number), 0) + 1
    INTO replacement_number
    FROM scoped_role_policy_versions;
    replacement_id := gen_random_uuid();

    INSERT INTO scoped_role_policy_versions (
        policy_version_id,
        version_number,
        policy_name,
        policy_status,
        source_name,
        source_sha256,
        policy_notes,
        created_by_user_id,
        published_by_user_id,
        created_at
    )
    SELECT
        replacement_id,
        replacement_number,
        policy_name || ' · Billing Audit History exclusion',
        'DRAFT',
        'migration_063_project_management_billing_role_access_repair',
        encode(digest('migration-063:' || replacement_number::text, 'sha256'), 'hex'),
        concat_ws(' ', NULLIF(policy_notes, ''), 'Migration 063 removed only Billing-family Module 008 grants. Accounting and every other role/module decision were preserved.'),
        created_by_user_id,
        published_by_user_id,
        NOW()
    FROM scoped_role_policy_versions
    WHERE policy_version_id = previous_id;

    INSERT INTO scoped_role_policy_grants (
        policy_version_id,
        role_code,
        module_code,
        action_code,
        scope_code,
        grant_effect,
        conditions,
        delegated_authority,
        reason_required,
        audit_required,
        source_designation,
        source_notes,
        is_active,
        created_at
    )
    SELECT
        replacement_id,
        role_code,
        module_code,
        action_code,
        scope_code,
        grant_effect,
        conditions,
        delegated_authority,
        reason_required,
        audit_required,
        source_designation,
        source_notes,
        is_active,
        NOW()
    FROM scoped_role_policy_grants
    WHERE policy_version_id = previous_id
      AND NOT (
          upper(role_code) IN ('BILLING', 'ACCOUNTING_BILLING', 'FINANCE')
          AND upper(module_code) = '008'
          AND upper(grant_effect) = 'GRANT'
      );

    UPDATE scoped_role_policy_versions
    SET policy_status = 'RETIRED',
        retired_at = NOW()
    WHERE policy_version_id = previous_id;

    UPDATE scoped_role_policy_versions
    SET policy_status = 'PUBLISHED',
        published_at = NOW()
    WHERE policy_version_id = replacement_id;

    INSERT INTO role_access_repair_063_policy_versions (
        singleton_key,
        previous_policy_version_id,
        replacement_policy_version_id,
        previous_version_number,
        replacement_version_number
    )
    VALUES (
        TRUE,
        previous_id,
        replacement_id,
        previous_number,
        replacement_number
    )
    ON CONFLICT (singleton_key) DO NOTHING;

    IF to_regclass('public.scoped_role_policy_audit_events') IS NOT NULL THEN
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
        VALUES (
            replacement_id,
            'MIGRATION_063_BILLING_AUDIT_HISTORY_EXCLUDED',
            NULL,
            'migration-063@projectpulse.local',
            'Remove Billing-family Module 008 grants while preserving Accounting and every unrelated policy decision.',
            jsonb_build_object('policyVersionId', previous_id, 'versionNumber', previous_number),
            jsonb_build_object('policyVersionId', replacement_id, 'versionNumber', replacement_number),
            jsonb_build_object('removedGrantCount', billing_grant_count, 'immutableAudit', TRUE)
        );
    END IF;
END;
$policy_repair$;

DO $assert_invariants$
DECLARE
    missing_pm_permissions INTEGER;
    billing_audit_permissions INTEGER;
BEGIN
    SELECT COUNT(*)
    INTO missing_pm_permissions
    FROM app_roles role
    CROSS JOIN projectpulse_063_pm_permissions required
    LEFT JOIN app_permissions permission
      ON upper(permission.permission_code) = upper(required.permission_code)
    LEFT JOIN app_role_permissions relationship
      ON relationship.app_role_id = role.app_role_id
     AND relationship.app_permission_id = permission.app_permission_id
    WHERE role.is_active = TRUE
      AND upper(role.role_code) IN (
          'PROJECT_MANAGER',
          'PROJECT_MANAGEMENT',
          'PROJECT_MANAGEMENT_LEAD',
          'PROJECT_MANAGEMENT_TEAM_LEAD',
          'PM_TEAM_LEAD'
      )
      AND (
            permission.app_permission_id IS NULL
         OR relationship.app_role_permission_id IS NULL
      );

    IF missing_pm_permissions <> 0 THEN
        RAISE EXCEPTION 'Migration 063 invariant failed: % Project Management permission relationship(s) are missing.', missing_pm_permissions;
    END IF;

    SELECT COUNT(*)
    INTO billing_audit_permissions
    FROM app_roles role
    JOIN app_role_permissions relationship
      ON relationship.app_role_id = role.app_role_id
    JOIN app_permissions permission
      ON permission.app_permission_id = relationship.app_permission_id
    WHERE role.is_active = TRUE
      AND upper(role.role_code) IN ('BILLING', 'ACCOUNTING_BILLING', 'FINANCE')
      AND (
            upper(COALESCE(permission.module_code, '')) = '008'
         OR upper(permission.permission_code) IN (
                'VIEW_AUDIT_TRAIL',
                'VIEW_AUDIT_HISTORY',
                'VIEW_AUDIT_HISTORY_008'
            )
      );

    IF billing_audit_permissions <> 0 THEN
        RAISE EXCEPTION 'Migration 063 invariant failed: % Billing Audit History permission(s) remain.', billing_audit_permissions;
    END IF;
END;
$assert_invariants$;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '063_project_management_billing_role_access_repair',
    'Grant Project Management own time, assigned-project approval, holiday, expense, and own qualification access; remove Billing Audit History access',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
