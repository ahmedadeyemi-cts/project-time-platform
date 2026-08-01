-- ProjectPulse migration 056A
-- Least-privilege guard for the role-workspace grants introduced by migration 056.
--
-- Migration 056 intentionally discovers module permissions dynamically so newly
-- registered modules become visible. This companion migration removes only the
-- non-view grants that migration 056 itself added to Project Management,
-- Accounting/Billing, and Sales roles unless the action is explicitly approved
-- for that role family. Existing pre-056 role grants are never removed here.

BEGIN;

DO $prerequisites$
BEGIN
    IF to_regclass('public.role_workspace_permission_changes_056') IS NULL THEN
        RAISE EXCEPTION 'Migration 056A requires migration 056_role_workspace_entra_crm_governance.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM schema_migrations
        WHERE migration_id = '056_role_workspace_entra_crm_governance'
    ) THEN
        RAISE EXCEPTION 'Migration 056A requires the registered migration 056 baseline.';
    END IF;
END;
$prerequisites$;

CREATE TABLE IF NOT EXISTS role_workspace_permission_scope_changes_056a (
    role_code VARCHAR(75) NOT NULL,
    permission_code VARCHAR(100) NOT NULL,
    change_kind VARCHAR(30) NOT NULL DEFAULT 'removed_056_grant'
        CHECK (change_kind = 'removed_056_grant'),
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (role_code, permission_code)
);

INSERT INTO role_workspace_permission_scope_changes_056a (
    role_code,
    permission_code,
    change_kind
)
SELECT
    change.role_code,
    change.permission_code,
    'removed_056_grant'
FROM role_workspace_permission_changes_056 change
WHERE change.change_kind = 'granted'
  AND upper(change.role_code) IN (
      'PROJECT_MANAGER',
      'PROJECT_MANAGEMENT',
      'PROJECT_MANAGEMENT_LEAD',
      'PROJECT_MANAGEMENT_TEAM_LEAD',
      'PM_TEAM_LEAD',
      'ACCOUNTING',
      'ACCOUNTING_BILLING',
      'BILLING',
      'FINANCE',
      'SALES',
      'INSIDE_SALES',
      'RESALE',
      'ACCOUNT_EXECUTIVE',
      'ACCOUNT_EXECUTIVES',
      'SALES_MANAGER'
  )
  AND NOT (
      upper(change.permission_code) LIKE 'VIEW\_%' ESCAPE '\'
      OR upper(change.permission_code) LIKE 'READ\_%' ESCAPE '\'
      OR upper(change.permission_code) LIKE 'EXPORT\_%' ESCAPE '\'
      OR upper(change.permission_code) IN (
          'MODULE_ACCESS',
          'MODULE_VIEW',
          'ACCESS_EXPLAIN',
          'APPROVE_TIME',
          'REJECT_TIME',
          'PROJECT_TIME_APPROVAL',
          'MANAGE_PROJECT_INTAKE',
          'MANAGE_RESOURCE_SCHEDULING',
          'MANAGE_PROJECT_DOCUMENTS',
          'MANAGE_ACCOUNT_RECONCILIATION'
      )
  )
ON CONFLICT DO NOTHING;

DELETE FROM app_role_permissions relationship
USING role_workspace_permission_scope_changes_056a change,
      app_roles role,
      app_permissions permission
WHERE upper(role.role_code) = upper(change.role_code)
  AND permission.permission_code = change.permission_code
  AND relationship.app_role_id = role.app_role_id
  AND relationship.app_permission_id = permission.app_permission_id;

CREATE OR REPLACE FUNCTION projectpulse_056a_block_immutable_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse_056a_immutable$
BEGIN
    RAISE EXCEPTION 'ProjectPulse migration 056A scope evidence is immutable.';
END;
$projectpulse_056a_immutable$;

DROP TRIGGER IF EXISTS trg_role_workspace_permission_scope_056a_immutable
    ON role_workspace_permission_scope_changes_056a;
CREATE TRIGGER trg_role_workspace_permission_scope_056a_immutable
BEFORE UPDATE OR DELETE ON role_workspace_permission_scope_changes_056a
FOR EACH ROW EXECUTE FUNCTION projectpulse_056a_block_immutable_mutation();

-- Explicit invariants: business roles may view Module 026 connection status,
-- but only actual administrators or a separately reviewed future grant may
-- manage connector credentials or OAuth configuration.
DO $assert_least_privilege$
DECLARE
    unsafe_count INTEGER;
BEGIN
    SELECT COUNT(*)
    INTO unsafe_count
    FROM app_role_permissions relationship
    JOIN app_roles role
      ON role.app_role_id = relationship.app_role_id
    JOIN app_permissions permission
      ON permission.app_permission_id = relationship.app_permission_id
    JOIN role_workspace_permission_changes_056 change
      ON upper(change.role_code) = upper(role.role_code)
     AND change.permission_code = permission.permission_code
     AND change.change_kind = 'granted'
    WHERE upper(role.role_code) IN (
        'ACCOUNTING', 'ACCOUNTING_BILLING', 'BILLING', 'FINANCE',
        'SALES', 'INSIDE_SALES', 'RESALE',
        'ACCOUNT_EXECUTIVE', 'ACCOUNT_EXECUTIVES', 'SALES_MANAGER'
    )
      AND permission.permission_code IN (
          'MANAGE_INTEGRATIONS_026',
          'MANAGE_ALL',
          'SYSTEM_ADMINISTRATION'
      );

    IF unsafe_count <> 0 THEN
        RAISE EXCEPTION 'Migration 056A least-privilege invariant failed: % unsafe business-role grants remain.', unsafe_count;
    END IF;
END;
$assert_least_privilege$;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '056a_role_workspace_permission_scope_guard',
    'Restrict migration-056 business-role module grants to view/read/export and explicitly approved scoped workflow actions',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
