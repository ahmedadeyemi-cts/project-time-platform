from __future__ import annotations

from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]

REGISTRY = ROOT / "src/frontend/project-time-web/src/module-availability-registry.js"
ROLE_MODEL = ROOT / "src/frontend/project-time-web/src/role-permission-model.js"
NAV_POLICY = ROOT / "src/frontend/project-time-web/src/module-navigation-access-policy.js"
VALIDATOR = ROOT / "src/frontend/project-time-web/scripts/validate-module-001a-engineer-request-closeout.mjs"
PR_WORKFLOW = ROOT / ".github/workflows/enterprise-ui-polish-ci.yml"
MIGRATION = ROOT / "database/migrations/089_module_catalog_role_administration_reconciliation.sql"
ROLLBACK = ROOT / "database/rollback/089_module_catalog_role_administration_reconciliation_rollback.sql"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def write(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def replace_once(path: Path, old: str, new: str) -> None:
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected exactly one replacement anchor, found {count}")
    write(path, text.replace(old, new, 1))


def sql(value: str) -> str:
    return value.replace("'", "''")


registry = read(REGISTRY)
pattern = re.compile(
    r"Object\.freeze\(\{\s*"
    r"moduleNumber:\s*'(?P<code>[^']+)'\s*,\s*"
    r"route:\s*'(?P<route>[^']+)'\s*,\s*"
    r"displayName:\s*'(?P<name>[^']+)'\s*,\s*"
    r"group:\s*'(?P<group>[^']+)'",
    re.S,
)
modules = [match.groupdict() for match in pattern.finditer(registry)]
if len(modules) < 70:
    raise RuntimeError(f"Expected at least 70 canonical modules, found {len(modules)}")
codes = [module["code"].upper() for module in modules]
if len(codes) != len(set(codes)):
    raise RuntimeError("Canonical module registry contains duplicate module numbers")
if "001A" not in codes:
    raise RuntimeError("Module 001A is missing from the canonical module registry")

catalog_values = ",\n".join(
    "    "
    + "("
    + ", ".join(
        [
            f"'{sql(module['code'].upper())}'",
            f"'{sql(module['name'])}'",
            f"'{sql(module['route'])}'",
            f"'{sql(module['group'])}'",
        ]
    )
    + ")"
    for module in modules
)

migration = f"""-- Pulse migration 089
-- Reconcile the complete built-in module catalog with Modules 012/037 and
-- restore Module 001A Engineer Request Closeout access for Engineer and
-- Engineering Lead roles.
--
-- This migration is additive, idempotent, and preserves every unrelated
-- published role-policy decision. It never widens Module 001A data beyond the
-- backend-enforced authenticated Engineer's own eligible assignments.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

DO $projectpulse089_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.app_roles') IS NULL
       OR to_regclass('public.app_permissions') IS NULL
       OR to_regclass('public.app_role_permissions') IS NULL
       OR to_regclass('public.scoped_role_policy_modules') IS NULL
       OR to_regclass('public.scoped_role_policy_actions') IS NULL
       OR to_regclass('public.scoped_role_policy_scopes') IS NULL
       OR to_regclass('public.scoped_role_policy_versions') IS NULL
       OR to_regclass('public.scoped_role_policy_grants') IS NULL THEN
        RAISE EXCEPTION 'Migration 089 requires the application RBAC and scoped role-policy foundations.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM schema_migrations
        WHERE migration_id = '078_module_001a_engineer_request_closeout'
    ) THEN
        RAISE EXCEPTION 'Migration 089 requires Module 001A migration 078 first.';
    END IF;
END;
$projectpulse089_prerequisites$;

CREATE TABLE IF NOT EXISTS module_catalog_reconciliation_089_modules (
    module_code TEXT PRIMARY KEY,
    was_present BOOLEAN NOT NULL,
    previous_module_name TEXT NULL,
    previous_route_scope TEXT NULL,
    previous_current_state TEXT NULL,
    previous_permission_notes TEXT NULL,
    previous_source_url TEXT NULL,
    previous_is_active BOOLEAN NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS module_catalog_reconciliation_089_permission_grants (
    app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE RESTRICT,
    app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    role_code TEXT NOT NULL,
    permission_code TEXT NOT NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (app_role_id, app_permission_id)
);

CREATE TABLE IF NOT EXISTS module_catalog_reconciliation_089_policy_versions (
    singleton_key BOOLEAN PRIMARY KEY DEFAULT TRUE CHECK (singleton_key),
    previous_policy_version_id UUID NOT NULL REFERENCES scoped_role_policy_versions(policy_version_id) ON DELETE RESTRICT,
    replacement_policy_version_id UUID NOT NULL REFERENCES scoped_role_policy_versions(policy_version_id) ON DELETE RESTRICT,
    previous_version_number INTEGER NOT NULL,
    replacement_version_number INTEGER NOT NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TEMP TABLE projectpulse_089_module_catalog (
    module_code TEXT PRIMARY KEY,
    module_name TEXT NOT NULL,
    route_scope TEXT NOT NULL,
    module_group TEXT NOT NULL
) ON COMMIT DROP;

INSERT INTO projectpulse_089_module_catalog (
    module_code,
    module_name,
    route_scope,
    module_group
)
VALUES
{catalog_values};

INSERT INTO module_catalog_reconciliation_089_modules (
    module_code,
    was_present,
    previous_module_name,
    previous_route_scope,
    previous_current_state,
    previous_permission_notes,
    previous_source_url,
    previous_is_active
)
SELECT
    desired.module_code,
    existing.module_code IS NOT NULL,
    existing.module_name,
    existing.route_scope,
    existing.current_state,
    existing.permission_notes,
    existing.source_url,
    existing.is_active
FROM projectpulse_089_module_catalog desired
LEFT JOIN scoped_role_policy_modules existing
  ON upper(existing.module_code) = upper(desired.module_code)
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
SELECT
    module_code,
    module_name,
    route_scope,
    'Installed',
    'Canonical Pulse module catalog entry · ' || module_group || '. Synchronized by migration 089.',
    'src/frontend/project-time-web/src/module-availability-registry.js',
    TRUE
FROM projectpulse_089_module_catalog
ON CONFLICT (module_code) DO UPDATE
SET module_name = EXCLUDED.module_name,
    route_scope = EXCLUDED.route_scope,
    current_state = EXCLUDED.current_state,
    permission_notes = EXCLUDED.permission_notes,
    source_url = EXCLUDED.source_url,
    is_active = TRUE;

CREATE TEMP TABLE projectpulse_089_role_permissions (
    role_code TEXT NOT NULL,
    permission_code TEXT NOT NULL,
    PRIMARY KEY (role_code, permission_code)
) ON COMMIT DROP;

INSERT INTO projectpulse_089_role_permissions (role_code, permission_code)
VALUES
    ('SUPER_ADMINISTRATOR', 'VIEW_ENGINEER_TASK_CLOSEOUT_001A'),
    ('SUPER_ADMINISTRATOR', 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A'),
    ('ENGINEER', 'VIEW_ENGINEER_TASK_CLOSEOUT_001A'),
    ('ENGINEER', 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A'),
    ('ENGINEERING', 'VIEW_ENGINEER_TASK_CLOSEOUT_001A'),
    ('ENGINEERING', 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A'),
    ('ENGINEERING_LEAD', 'VIEW_ENGINEER_TASK_CLOSEOUT_001A'),
    ('ENGINEERING_LEAD', 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A'),
    ('ENGINEERING_TEAM_LEAD', 'VIEW_ENGINEER_TASK_CLOSEOUT_001A'),
    ('ENGINEERING_TEAM_LEAD', 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A');

INSERT INTO module_catalog_reconciliation_089_permission_grants (
    app_role_id,
    app_permission_id,
    role_code,
    permission_code
)
SELECT
    role.app_role_id,
    permission.app_permission_id,
    upper(role.role_code),
    permission.permission_code
FROM projectpulse_089_role_permissions desired
JOIN app_roles role
  ON upper(role.role_code) = desired.role_code
 AND role.is_active = TRUE
JOIN app_permissions permission
  ON upper(permission.permission_code) = desired.permission_code
LEFT JOIN app_role_permissions existing
  ON existing.app_role_id = role.app_role_id
 AND existing.app_permission_id = permission.app_permission_id
WHERE existing.app_role_permission_id IS NULL
ON CONFLICT (app_role_id, app_permission_id) DO NOTHING;

INSERT INTO app_role_permissions (
    app_role_id,
    app_permission_id,
    created_at
)
SELECT
    app_role_id,
    app_permission_id,
    NOW()
FROM module_catalog_reconciliation_089_permission_grants
ON CONFLICT (app_role_id, app_permission_id) DO NOTHING;

CREATE TEMP TABLE projectpulse_089_module001a_grants (
    role_code TEXT NOT NULL,
    action_code TEXT NOT NULL,
    scope_code TEXT NOT NULL,
    reason_required BOOLEAN NOT NULL DEFAULT FALSE,
    PRIMARY KEY (role_code, action_code, scope_code)
) ON COMMIT DROP;

INSERT INTO projectpulse_089_module001a_grants (
    role_code,
    action_code,
    scope_code,
    reason_required
)
VALUES
    ('ENGINEERING', 'MODULE_ACCESS', 'ORGANIZATION', FALSE),
    ('ENGINEERING', 'MODULE_VIEW', 'SELF', FALSE),
    ('ENGINEERING', 'RECORD_CREATE', 'SELF', FALSE),
    ('ENGINEERING', 'RECORD_EDIT', 'SELF', FALSE),
    ('ENGINEERING', 'RECORD_REOPEN', 'SELF', TRUE),
    ('ENGINEERING', 'WORKFLOW_MANAGE', 'SELF', FALSE),
    ('ENGINEERING', 'AUDIT_VIEW', 'SELF', FALSE),
    ('ENGINEERING_LEAD', 'MODULE_ACCESS', 'ORGANIZATION', FALSE),
    ('ENGINEERING_LEAD', 'MODULE_VIEW', 'SELF', FALSE),
    ('ENGINEERING_LEAD', 'RECORD_CREATE', 'SELF', FALSE),
    ('ENGINEERING_LEAD', 'RECORD_EDIT', 'SELF', FALSE),
    ('ENGINEERING_LEAD', 'RECORD_REOPEN', 'SELF', TRUE),
    ('ENGINEERING_LEAD', 'WORKFLOW_MANAGE', 'SELF', FALSE),
    ('ENGINEERING_LEAD', 'AUDIT_VIEW', 'SELF', FALSE);

DO $projectpulse089_validate_catalog_dependencies$
DECLARE
    missing_actions INTEGER;
    missing_scopes INTEGER;
BEGIN
    SELECT COUNT(*)
    INTO missing_actions
    FROM (
        SELECT DISTINCT action_code
        FROM projectpulse_089_module001a_grants
    ) desired
    LEFT JOIN scoped_role_policy_actions action
      ON upper(action.action_code) = upper(desired.action_code)
     AND action.is_active = TRUE
    WHERE action.action_code IS NULL;

    IF missing_actions <> 0 THEN
        RAISE EXCEPTION 'Migration 089 cannot publish Module 001A grants because % scoped action(s) are unavailable.', missing_actions;
    END IF;

    SELECT COUNT(*)
    INTO missing_scopes
    FROM (
        SELECT DISTINCT scope_code
        FROM projectpulse_089_module001a_grants
    ) desired
    LEFT JOIN scoped_role_policy_scopes scope
      ON upper(scope.scope_code) = upper(desired.scope_code)
     AND scope.is_active = TRUE
    WHERE scope.scope_code IS NULL;

    IF missing_scopes <> 0 THEN
        RAISE EXCEPTION 'Migration 089 cannot publish Module 001A grants because % scoped data scope(s) are unavailable.', missing_scopes;
    END IF;
END;
$projectpulse089_validate_catalog_dependencies$;

DO $projectpulse089_publish_policy$
DECLARE
    previous_id UUID;
    previous_number INTEGER;
    replacement_id UUID;
    replacement_number INTEGER;
    recorded_replacement_id UUID;
    missing_grants INTEGER;
    conflicting_denies INTEGER;
BEGIN
    SELECT replacement_policy_version_id
    INTO recorded_replacement_id
    FROM module_catalog_reconciliation_089_policy_versions
    WHERE singleton_key = TRUE;

    IF recorded_replacement_id IS NOT NULL THEN
        RETURN;
    END IF;

    SELECT policy_version_id, version_number
    INTO previous_id, previous_number
    FROM scoped_role_policy_versions
    WHERE policy_status = 'PUBLISHED'
    ORDER BY version_number DESC
    LIMIT 1;

    IF previous_id IS NULL THEN
        RAISE EXCEPTION 'Migration 089 requires one published scoped role-policy version.';
    END IF;

    SELECT COUNT(*)
    INTO missing_grants
    FROM projectpulse_089_module001a_grants desired
    LEFT JOIN scoped_role_policy_grants existing
      ON existing.policy_version_id = previous_id
     AND upper(existing.role_code) = desired.role_code
     AND upper(existing.module_code) = '001A'
     AND upper(existing.action_code) = desired.action_code
     AND upper(existing.scope_code) = desired.scope_code
     AND upper(existing.grant_effect) = 'GRANT'
     AND existing.is_active = TRUE
    WHERE existing.scoped_role_policy_grant_id IS NULL;

    SELECT COUNT(*)
    INTO conflicting_denies
    FROM scoped_role_policy_grants
    WHERE policy_version_id = previous_id
      AND upper(role_code) IN ('ENGINEERING', 'ENGINEERING_LEAD')
      AND upper(module_code) = '001A'
      AND upper(grant_effect) = 'DENY'
      AND is_active = TRUE;

    IF missing_grants = 0 AND conflicting_denies = 0 THEN
        RETURN;
    END IF;

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
        policy_name || ' · Module 001A access reconciliation',
        'DRAFT',
        'migration_089_module_catalog_role_administration_reconciliation',
        encode(digest('migration-089:' || replacement_number::text, 'sha256'), 'hex'),
        concat_ws(
            ' ',
            NULLIF(policy_notes, ''),
            'Migration 089 synchronized every built-in Pulse module into Role Administration and granted Engineer/Engineering Lead access to Module 001A while preserving backend self-scope enforcement.'
        ),
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
          upper(module_code) = '001A'
          AND upper(role_code) IN ('ENGINEERING', 'ENGINEER', 'ENGINEERING_LEAD', 'ENGINEERING_TEAM_LEAD')
          AND (
              upper(action_code) = 'MODULE_ACCESS'
              OR upper(grant_effect) = 'DENY'
          )
      );

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
        desired.role_code,
        '001A',
        desired.action_code,
        desired.scope_code,
        'GRANT',
        jsonb_build_object(
            'source', 'Migration 089 Module 001A Role Administration reconciliation',
            'designation', 'Manage',
            'permissionLevel', 'Manage',
            'scopeCode', desired.scope_code,
            'moduleScopedOnly', FALSE,
            'engineerOwnedOnly', TRUE,
            'allowedWorkTypes', jsonb_build_array('SERVICE_REQUEST', 'PRESALES', 'INTERNAL')
        ),
        FALSE,
        desired.reason_required,
        desired.action_code <> 'MODULE_VIEW',
        'Manage',
        'Engineer-owned Service Request, Pre-Sales, and Internal closeout. Server endpoints remain limited to the authenticated Engineer''s own assignment.',
        TRUE,
        NOW()
    FROM projectpulse_089_module001a_grants desired
    ON CONFLICT (
        policy_version_id,
        role_code,
        module_code,
        action_code,
        scope_code,
        grant_effect
    ) DO NOTHING;

    INSERT INTO module_catalog_reconciliation_089_policy_versions (
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

    UPDATE scoped_role_policy_versions
    SET policy_status = 'RETIRED',
        retired_at = NOW()
    WHERE policy_version_id = previous_id;

    UPDATE scoped_role_policy_versions
    SET policy_status = 'PUBLISHED',
        published_at = NOW()
    WHERE policy_version_id = replacement_id;

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
            'MIGRATION_089_MODULE001A_ROLE_CATALOG_RECONCILED',
            NULL,
            'migration-089@pulse.local',
            'Synchronize built-in modules and restore Module 001A access for Engineer and Engineering Lead.',
            jsonb_build_object(
                'policyVersionId', previous_id,
                'versionNumber', previous_number
            ),
            jsonb_build_object(
                'policyVersionId', replacement_id,
                'versionNumber', replacement_number
            ),
            jsonb_build_object(
                'moduleCode', '001A',
                'roles', jsonb_build_array('ENGINEERING', 'ENGINEERING_LEAD'),
                'permissionLevel', 'Manage',
                'scope', 'SELF',
                'catalogModuleCount', (SELECT COUNT(*) FROM projectpulse_089_module_catalog),
                'immutableAudit', TRUE
            )
        );
    END IF;
END;
$projectpulse089_publish_policy$;

DO $projectpulse089_assertions$
DECLARE
    catalog_mismatches INTEGER;
    permission_mismatches INTEGER;
    policy_mismatches INTEGER;
    conflicting_denies INTEGER;
    published_id UUID;
BEGIN
    SELECT COUNT(*)
    INTO catalog_mismatches
    FROM projectpulse_089_module_catalog desired
    LEFT JOIN scoped_role_policy_modules actual
      ON upper(actual.module_code) = upper(desired.module_code)
    WHERE actual.module_code IS NULL
       OR actual.module_name IS DISTINCT FROM desired.module_name
       OR actual.route_scope IS DISTINCT FROM desired.route_scope
       OR actual.is_active IS DISTINCT FROM TRUE;

    IF catalog_mismatches <> 0 THEN
        RAISE EXCEPTION 'Migration 089 invariant failed: % built-in module catalog row(s) are missing or inconsistent.', catalog_mismatches;
    END IF;

    SELECT COUNT(*)
    INTO permission_mismatches
    FROM projectpulse_089_role_permissions desired
    JOIN app_roles role
      ON upper(role.role_code) = desired.role_code
     AND role.is_active = TRUE
    LEFT JOIN app_permissions permission
      ON upper(permission.permission_code) = desired.permission_code
    LEFT JOIN app_role_permissions relationship
      ON relationship.app_role_id = role.app_role_id
     AND relationship.app_permission_id = permission.app_permission_id
    WHERE permission.app_permission_id IS NULL
       OR relationship.app_role_permission_id IS NULL;

    IF permission_mismatches <> 0 THEN
        RAISE EXCEPTION 'Migration 089 invariant failed: % Module 001A application-role permission relationship(s) are missing.', permission_mismatches;
    END IF;

    SELECT policy_version_id
    INTO published_id
    FROM scoped_role_policy_versions
    WHERE policy_status = 'PUBLISHED'
    ORDER BY version_number DESC
    LIMIT 1;

    SELECT COUNT(*)
    INTO policy_mismatches
    FROM projectpulse_089_module001a_grants desired
    LEFT JOIN scoped_role_policy_grants actual
      ON actual.policy_version_id = published_id
     AND upper(actual.role_code) = desired.role_code
     AND upper(actual.module_code) = '001A'
     AND upper(actual.action_code) = desired.action_code
     AND upper(actual.scope_code) = desired.scope_code
     AND upper(actual.grant_effect) = 'GRANT'
     AND actual.is_active = TRUE
    WHERE actual.scoped_role_policy_grant_id IS NULL;

    SELECT COUNT(*)
    INTO conflicting_denies
    FROM scoped_role_policy_grants
    WHERE policy_version_id = published_id
      AND upper(role_code) IN ('ENGINEERING', 'ENGINEERING_LEAD')
      AND upper(module_code) = '001A'
      AND upper(grant_effect) = 'DENY'
      AND is_active = TRUE;

    IF policy_mismatches <> 0 OR conflicting_denies <> 0 THEN
        RAISE EXCEPTION
            'Migration 089 invariant failed: % required Module 001A grant(s) are missing and % conflicting deny row(s) remain.',
            policy_mismatches,
            conflicting_denies;
    END IF;
END;
$projectpulse089_assertions$;

INSERT INTO schema_migrations (
    migration_id,
    description,
    applied_at
)
VALUES (
    '089_module_catalog_role_administration_reconciliation',
    'Synchronize all built-in Pulse modules into Role Administration and restore Module 001A Engineer/Engineering Lead access',
    NOW()
)
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
"""

rollback = """-- Rollback for Pulse migration 089.
--
-- Guardrail: a later published role-policy version must be handled separately.
-- The rollback refuses to overwrite newer authorization decisions.

BEGIN;

DO $projectpulse089_rollback_policy$
DECLARE
    previous_id UUID;
    replacement_id UUID;
    current_published_id UUID;
BEGIN
    IF to_regclass('public.module_catalog_reconciliation_089_policy_versions') IS NULL THEN
        RETURN;
    END IF;

    SELECT
        previous_policy_version_id,
        replacement_policy_version_id
    INTO
        previous_id,
        replacement_id
    FROM module_catalog_reconciliation_089_policy_versions
    WHERE singleton_key = TRUE;

    IF previous_id IS NULL OR replacement_id IS NULL THEN
        RETURN;
    END IF;

    SELECT policy_version_id
    INTO current_published_id
    FROM scoped_role_policy_versions
    WHERE policy_status = 'PUBLISHED'
    ORDER BY version_number DESC
    LIMIT 1;

    IF current_published_id = replacement_id THEN
        UPDATE scoped_role_policy_versions
        SET policy_status = 'RETIRED',
            retired_at = NOW()
        WHERE policy_version_id = replacement_id;

        UPDATE scoped_role_policy_versions
        SET policy_status = 'PUBLISHED',
            retired_at = NULL
        WHERE policy_version_id = previous_id;

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
                previous_id,
                'ROLLBACK_089_MODULE001A_ROLE_CATALOG_RESTORED',
                NULL,
                'rollback-089@pulse.local',
                'Restore the scoped role-policy version that preceded migration 089.',
                jsonb_build_object('policyVersionId', replacement_id),
                jsonb_build_object('policyVersionId', previous_id),
                jsonb_build_object('immutableAudit', TRUE)
            );
        END IF;
    ELSIF current_published_id IS DISTINCT FROM previous_id THEN
        RAISE EXCEPTION
            'Rollback 089 refused: a newer scoped role-policy version (%) is published.',
            current_published_id;
    END IF;
END;
$projectpulse089_rollback_policy$;

DO $projectpulse089_rollback_permissions$
BEGIN
    IF to_regclass('public.module_catalog_reconciliation_089_permission_grants') IS NULL THEN
        RETURN;
    END IF;

    DELETE FROM app_role_permissions relationship
    USING module_catalog_reconciliation_089_permission_grants introduced
    WHERE relationship.app_role_id = introduced.app_role_id
      AND relationship.app_permission_id = introduced.app_permission_id;
END;
$projectpulse089_rollback_permissions$;

DO $projectpulse089_rollback_modules$
BEGIN
    IF to_regclass('public.module_catalog_reconciliation_089_modules') IS NULL THEN
        RETURN;
    END IF;

    UPDATE scoped_role_policy_modules module
    SET module_name = evidence.previous_module_name,
        route_scope = evidence.previous_route_scope,
        current_state = evidence.previous_current_state,
        permission_notes = evidence.previous_permission_notes,
        source_url = evidence.previous_source_url,
        is_active = evidence.previous_is_active
    FROM module_catalog_reconciliation_089_modules evidence
    WHERE evidence.was_present = TRUE
      AND module.module_code = evidence.module_code;

    UPDATE scoped_role_policy_modules module
    SET current_state = 'Rolled back',
        permission_notes = 'Migration 089 registration rolled back. The inactive row is retained because immutable policy history may reference it.',
        is_active = FALSE
    FROM module_catalog_reconciliation_089_modules evidence
    WHERE evidence.was_present = FALSE
      AND module.module_code = evidence.module_code;
END;
$projectpulse089_rollback_modules$;

DELETE FROM schema_migrations
WHERE migration_id = '089_module_catalog_role_administration_reconciliation';

DROP TABLE IF EXISTS module_catalog_reconciliation_089_permission_grants;
DROP TABLE IF EXISTS module_catalog_reconciliation_089_policy_versions;
DROP TABLE IF EXISTS module_catalog_reconciliation_089_modules;

COMMIT;
"""

write(MIGRATION, migration)
write(ROLLBACK, rollback)

# Insert a module-specific preset after the Module 001 definition and before Module 002.
role_text = read(ROLE_MODEL)
anchor = """  },
  '002': {
"""
module001a_special = """  },
  '001A': {
    View: ['MODULE_VIEW'],
    'Create/Edit': ['MODULE_VIEW', 'RECORD_CREATE', 'RECORD_EDIT'],
    Approve: ['MODULE_VIEW', 'RECORD_CREATE', 'RECORD_EDIT', 'RECORD_REOPEN', 'WORKFLOW_MANAGE', 'AUDIT_VIEW'],
    Manage: ['MODULE_VIEW', 'RECORD_CREATE', 'RECORD_EDIT', 'RECORD_REOPEN', 'WORKFLOW_MANAGE', 'AUDIT_VIEW'],
    Administer: ['MODULE_VIEW', 'RECORD_CREATE', 'RECORD_EDIT', 'RECORD_REOPEN', 'WORKFLOW_MANAGE', 'AUDIT_VIEW', 'AUDIT_RECORD'],
    'Full Control': ['MODULE_VIEW', 'RECORD_CREATE', 'RECORD_EDIT', 'RECORD_REOPEN', 'WORKFLOW_MANAGE', 'AUDIT_VIEW', 'AUDIT_RECORD']
  },
  '002': {
"""
if "'001A': {" not in role_text:
    if role_text.count(anchor) != 1:
        raise RuntimeError(f"{ROLE_MODEL}: Module 001A insertion anchor mismatch")
    role_text = role_text.replace(anchor, module001a_special, 1)
    write(ROLE_MODEL, role_text)

replace_once(
    ROLE_MODEL,
    """  if (level === 'No Access') {
    return [{ actionCode: 'MODULE_ACCESS', scopeCode: 'ORGANIZATION', effect: 'DENY', conditions, delegatedAuthority: false, reasonRequired: false, auditRequired: true, isActive: true }];
  }
""",
    """  if (level === 'No Access') {
    return [{ actionCode: 'MODULE_ACCESS', scopeCode: 'ORGANIZATION', effect: 'DENY', conditions: { ...conditions, scopeCode: 'ORGANIZATION' }, delegatedAuthority: false, reasonRequired: false, auditRequired: true, isActive: true }];
  }
""",
)

replace_once(
    ROLE_MODEL,
    """  if (role === 'PROJECT_TEAM_COORDINATOR') {
    const excluded = new Set(['MODULE_CONFIGURE', 'POLICY_DELEGATE', 'POLICY_PUBLISH', 'POLICY_RESTORE', 'SYSTEM_CONFIGURE', 'TIME_SUBMIT', 'TIME_DELETE_PERMANENT']);
    actions = actions.filter((actionCode) => !excluded.has(actionCode));
  }

  return actions.map((actionCode) => ({ actionCode, scopeCode: scope, effect: 'GRANT', conditions, ...flags(actionCode, role) }));
""",
    """  if (role === 'PROJECT_TEAM_COORDINATOR') {
    const excluded = new Set(['MODULE_CONFIGURE', 'POLICY_DELEGATE', 'POLICY_PUBLISH', 'POLICY_RESTORE', 'SYSTEM_CONFIGURE', 'TIME_SUBMIT', 'TIME_DELETE_PERMANENT']);
    actions = actions.filter((actionCode) => !excluded.has(actionCode));
  }

  actions = [...new Set(['MODULE_ACCESS', ...actions])];

  return actions.map((actionCode) => {
    const grantScope = actionCode === 'MODULE_ACCESS' ? 'ORGANIZATION' : scope;
    return {
      actionCode,
      scopeCode: grantScope,
      effect: 'GRANT',
      conditions: { ...conditions, scopeCode: grantScope },
      ...flags(actionCode, role)
    };
  });
""",
)

replace_once(
    NAV_POLICY,
    """      if (!roleSet.has(roleCodeOf(grant))) continue;
      if (canonicalRoleCode(grant?.actionCode ?? grant?.ActionCode) !== 'MODULE_ACCESS') continue;
      const moduleCode = moduleCodeOf(grant);
""",
    """      if (!roleSet.has(roleCodeOf(grant))) continue;
      const actionCode = canonicalRoleCode(grant?.actionCode ?? grant?.ActionCode);
      if (!['MODULE_ACCESS', 'MODULE_VIEW'].includes(actionCode)) continue;
      const moduleCode = moduleCodeOf(grant);
""",
)

validator = read(VALIDATOR)
validator = validator.replace(
    """const migration = read('database/migrations/078_module_001a_engineer_request_closeout.sql');
const rollback = read('database/rollback/078_module_001a_engineer_request_closeout_rollback.sql');
""",
    """const migration = read('database/migrations/078_module_001a_engineer_request_closeout.sql');
const rollback = read('database/rollback/078_module_001a_engineer_request_closeout_rollback.sql');
const catalogMigration = read('database/migrations/089_module_catalog_role_administration_reconciliation.sql');
const catalogRollback = read('database/rollback/089_module_catalog_role_administration_reconciliation_rollback.sql');
""",
    1,
)
validator = validator.replace(
    """const registry = read('src/frontend/project-time-web/src/module-availability-registry.js');
const ui = read('src/frontend/project-time-web/src/EngineerTaskCloseoutCenter.jsx');
""",
    """const registry = read('src/frontend/project-time-web/src/module-availability-registry.js');
const rolePermissionModel = read('src/frontend/project-time-web/src/role-permission-model.js');
const navigationPolicy = read('src/frontend/project-time-web/src/module-navigation-access-policy.js');
const ui = read('src/frontend/project-time-web/src/EngineerTaskCloseoutCenter.jsx');
""",
    1,
)
validator_anchor = """requireText(migration, "'#engineer-task-closeout'", 'feature registration');

requireText(rollback, 'Rollback refused: Module 001A closeout records exist.', 'guarded rollback');
"""
validator_block = """requireText(migration, "'#engineer-task-closeout'", 'feature registration');

const registryModules = [...registry.matchAll(
  /Object\\.freeze\\(\\{\\s*moduleNumber:\\s*'([^']+)'\\s*,\\s*route:\\s*'([^']+)'\\s*,\\s*displayName:\\s*'([^']+)'\\s*,\\s*group:\\s*'([^']+)'/gs
)].map((match) => ({ moduleCode: match[1].toUpperCase(), route: match[2], moduleName: match[3], group: match[4] }));
if (registryModules.length < 70) {
  failures.push(`module catalog reconciliation: expected at least 70 canonical modules, found ${registryModules.length}`);
}
if (new Set(registryModules.map((module) => module.moduleCode)).size !== registryModules.length) {
  failures.push('module catalog reconciliation: canonical module numbers must be unique');
}
const sqlQuote = (value) => String(value).replaceAll("'", "''");
for (const module of registryModules) {
  requireText(
    catalogMigration,
    `('${sqlQuote(module.moduleCode)}', '${sqlQuote(module.moduleName)}', '${sqlQuote(module.route)}', '${sqlQuote(module.group)}')`,
    `Role Administration catalog registration for Module ${module.moduleCode}`
  );
}
for (const roleCode of ['ENGINEER', 'ENGINEERING', 'ENGINEERING_LEAD', 'ENGINEERING_TEAM_LEAD']) {
  requireText(catalogMigration, `('${roleCode}', 'VIEW_ENGINEER_TASK_CLOSEOUT_001A')`, `${roleCode} view permission repair`);
  requireText(catalogMigration, `('${roleCode}', 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A')`, `${roleCode} manage permission repair`);
}
for (const roleCode of ['ENGINEERING', 'ENGINEERING_LEAD']) {
  requireText(catalogMigration, `('${roleCode}', 'MODULE_ACCESS', 'ORGANIZATION', FALSE)`, `${roleCode} module access grant`);
  requireText(catalogMigration, `('${roleCode}', 'WORKFLOW_MANAGE', 'SELF', FALSE)`, `${roleCode} self-scoped closeout workflow grant`);
}
requireText(catalogMigration, 'migration_089_module_catalog_role_administration_reconciliation', 'immutable policy source');
requireText(catalogMigration, "'allowedWorkTypes', jsonb_build_array('SERVICE_REQUEST', 'PRESALES', 'INTERNAL')", 'eligible request types');
requireText(catalogMigration, 'engineerOwnedOnly', 'own-assignment policy evidence');
requireText(catalogRollback, 'Rollback 089 refused: a newer scoped role-policy version', 'guarded policy rollback');
requireText(rolePermissionModel, "'001A': {", 'Module 001A intuitive permission preset');
requireText(rolePermissionModel, "actions = [...new Set(['MODULE_ACCESS', ...actions])]", 'non-No Access presets grant module visibility');
requireText(rolePermissionModel, "actionCode === 'MODULE_ACCESS' ? 'ORGANIZATION' : scope", 'organization module-access scope');
requireText(navigationPolicy, "['MODULE_ACCESS', 'MODULE_VIEW'].includes(actionCode)", 'legacy published Module View visibility compatibility');

requireText(rollback, 'Rollback refused: Module 001A closeout records exist.', 'guarded rollback');
"""
if validator.count(validator_anchor) != 1:
    raise RuntimeError(f"{VALIDATOR}: catalog validation anchor mismatch")
validator = validator.replace(validator_anchor, validator_block, 1)

backend_role_anchor = """requireText(backend, 'pa.user_id = @engineer_user_id', 'own-assignment server scope');
requireText(backend, 'reason.Length < 10', 'required reopen reason');
"""
backend_role_block = """requireText(backend, 'pa.user_id = @engineer_user_id', 'own-assignment server scope');
for (const roleCode of ['ENGINEER', 'ENGINEERING', 'ENGINEERING_LEAD', 'ENGINEERING_TEAM_LEAD']) {
  requireText(backend, `"${roleCode}"`, `${roleCode} runtime access`);
}
for (const normalizedWorkType of ["'servicerequest'", "'presales'", "'internal'"]) {
  requireText(backend, normalizedWorkType, `${normalizedWorkType} closeout eligibility`);
}
requireText(backend, 'reason.Length < 10', 'required reopen reason');
"""
if validator.count(backend_role_anchor) != 1:
    raise RuntimeError(f"{VALIDATOR}: backend role validation anchor mismatch")
validator = validator.replace(backend_role_anchor, backend_role_block, 1)
write(VALIDATOR, validator)

workflow = read(PR_WORKFLOW)
workflow = workflow.replace(
    "name: Enterprise UI and On-Call Follow-up CI",
    "name: Enterprise UI, On-Call, and Module 001A RBAC Follow-up CI",
    1,
)
workflow = workflow.replace(
    """      - 'docs/modules/module-072-oneassist-routing-directory/**'
""",
    """      - 'docs/modules/module-072-oneassist-routing-directory/**'
      - 'database/migrations/089_module_catalog_role_administration_reconciliation.sql'
      - 'database/rollback/089_module_catalog_role_administration_reconciliation_rollback.sql'
      - 'src/frontend/project-time-web/scripts/validate-module-001a-engineer-request-closeout.mjs'
      - 'src/frontend/project-time-web/src/role-permission-model.js'
      - 'src/frontend/project-time-web/src/module-navigation-access-policy.js'
""",
    1,
)
workflow = workflow.replace(
    "name: Validate Appearance, Celar controls, On-Call access, and OneAssist boundary",
    "name: Validate Appearance, Celar controls, On-Call access, and Module 001A RBAC",
    1,
)
workflow = workflow.replace(
    """          docs/modules/module-072-oneassist-routing-directory/README.md
""",
    """          docs/modules/module-072-oneassist-routing-directory/README.md
          database/migrations/089_module_catalog_role_administration_reconciliation.sql
          database/rollback/089_module_catalog_role_administration_reconciliation_rollback.sql
""",
    1,
)
workflow = workflow.replace(
    """          src/frontend/project-time-web/scripts/validate-module-072-oneassist-routing-directory.mjs
""",
    """          src/frontend/project-time-web/scripts/validate-module-001a-engineer-request-closeout.mjs
          src/frontend/project-time-web/scripts/validate-module-072-oneassist-routing-directory.mjs
""",
    1,
)
workflow = workflow.replace(
    """          src/frontend/project-time-web/src/main.jsx
""",
    """          src/frontend/project-time-web/src/main.jsx
          src/frontend/project-time-web/src/module-navigation-access-policy.js
          src/frontend/project-time-web/src/role-permission-model.js
""",
    1,
)
workflow = workflow.replace(
    """          node --check ./scripts/validate-module-071-oncall-scheduling.mjs
          node --check ./scripts/validate-module-072-oneassist-routing-directory.mjs
""",
    """          node --check ./scripts/validate-module-001a-engineer-request-closeout.mjs
          node --check ./scripts/validate-module-071-oncall-scheduling.mjs
          node --check ./scripts/validate-module-072-oneassist-routing-directory.mjs
          node --check ./src/module-navigation-access-policy.js
          node --check ./src/role-permission-model.js
""",
    1,
)
workflow = workflow.replace(
    """          node ./scripts/validate-enterprise-ui-polish.mjs
          node ./scripts/validate-module-071-oncall-scheduling.mjs
""",
    """          node ./scripts/validate-enterprise-ui-polish.mjs
          node ./scripts/validate-module-001a-engineer-request-closeout.mjs
          node ./scripts/validate-module-071-oncall-scheduling.mjs
""",
    1,
)
write(PR_WORKFLOW, workflow)

print(f"MODULE_CATALOG_COUNT={len(modules)}")
print("PR698_MODULE001A_ROLE_CATALOG_PATCH=READY")
