-- ProjectPulse migration 070 rollback
-- Removes Module 033 Project Forge operational storage and only the catalog and
-- RBAC rows proven to have been created by migration 070. Canonical projects,
-- project_tasks, project_assignments, Module 065 dispatch evidence, and Module
-- 064 administrator-owned routes are never rewritten.

BEGIN;

-- Project Forge plans, estimates, canonical adoption links, and audit records
-- are operational evidence. A rollback must never erase them implicitly. The
-- operator must first archive/export and remove those records through an
-- explicitly reviewed data-retention procedure.
DO $projectpulse070_operational_guard$
DECLARE
    relation_name TEXT;
    row_exists BOOLEAN;
BEGIN
    FOREACH relation_name IN ARRAY ARRAY[
        'project_forge_plans',
        'project_forge_plan_tasks',
        'project_forge_plan_assignments',
        'project_forge_task_dependencies',
        'project_forge_task_details',
        'project_forge_audit_events'
    ]
    LOOP
        IF to_regclass('public.' || relation_name) IS NOT NULL THEN
            EXECUTE format('SELECT EXISTS (SELECT 1 FROM %I LIMIT 1)', relation_name)
            INTO row_exists;
            IF row_exists THEN
                RAISE EXCEPTION 'Project Forge migration 070 rollback blocked: operational or immutable audit data exists in %.', relation_name;
            END IF;
        END IF;
    END LOOP;
END;
$projectpulse070_operational_guard$;

CREATE TEMP TABLE projectpulse070_permissions_to_remove (
    app_permission_id UUID PRIMARY KEY
) ON COMMIT DROP;
CREATE TEMP TABLE projectpulse070_grants_to_remove (
    app_role_id UUID NOT NULL,
    app_permission_id UUID NOT NULL,
    PRIMARY KEY (app_role_id, app_permission_id)
) ON COMMIT DROP;
CREATE TEMP TABLE projectpulse070_policies_to_remove (
    policy_code VARCHAR(160) PRIMARY KEY
) ON COMMIT DROP;
CREATE TEMP TABLE projectpulse070_routes_to_remove (
    feature_code TEXT PRIMARY KEY
) ON COMMIT DROP;
CREATE TEMP TABLE projectpulse070_features_to_remove (
    feature_code VARCHAR(100) PRIMARY KEY
) ON COMMIT DROP;

DO $projectpulse070_capture_evidence$
BEGIN
    IF to_regclass('public.project_forge_070_permissions_created') IS NOT NULL THEN
        INSERT INTO projectpulse070_permissions_to_remove(app_permission_id)
        SELECT app_permission_id FROM project_forge_070_permissions_created
        ON CONFLICT DO NOTHING;
    END IF;
    IF to_regclass('public.project_forge_070_role_grants') IS NOT NULL THEN
        INSERT INTO projectpulse070_grants_to_remove(app_role_id, app_permission_id)
        SELECT app_role_id, app_permission_id FROM project_forge_070_role_grants
        ON CONFLICT DO NOTHING;
    END IF;
    IF to_regclass('public.project_forge_070_notification_policies_created') IS NOT NULL THEN
        INSERT INTO projectpulse070_policies_to_remove(policy_code)
        SELECT policy_code FROM project_forge_070_notification_policies_created
        ON CONFLICT DO NOTHING;
    END IF;
    IF to_regclass('public.project_forge_070_ai_routes_created') IS NOT NULL THEN
        INSERT INTO projectpulse070_routes_to_remove(feature_code)
        SELECT feature_code FROM project_forge_070_ai_routes_created
        ON CONFLICT DO NOTHING;
    END IF;
    IF to_regclass('public.project_forge_070_features_created') IS NOT NULL THEN
        INSERT INTO projectpulse070_features_to_remove(feature_code)
        SELECT feature_code FROM project_forge_070_features_created
        ON CONFLICT DO NOTHING;
    END IF;
END;
$projectpulse070_capture_evidence$;

-- Evidence is already copied into transaction-local tables. Remove the evidence
-- foreign keys before deleting the migration-created RBAC catalog rows.
DROP TABLE IF EXISTS project_forge_070_features_created;
DROP TABLE IF EXISTS project_forge_070_ai_routes_created;
DROP TABLE IF EXISTS project_forge_070_notification_policies_created;
DROP TABLE IF EXISTS project_forge_070_role_grants;
DROP TABLE IF EXISTS project_forge_070_permissions_created;

-- Remove only grants inserted by migration 070. Later grants and permissions
-- that predated the migration are retained.
DELETE FROM app_role_permissions relationship
USING projectpulse070_grants_to_remove evidence
WHERE relationship.app_role_id = evidence.app_role_id
  AND relationship.app_permission_id = evidence.app_permission_id;

DO $projectpulse070_remove_feature$
BEGIN
    IF to_regclass('public.app_feature_catalog') IS NOT NULL THEN
        DELETE FROM app_feature_catalog feature
        USING projectpulse070_features_to_remove evidence
        WHERE feature.feature_code = evidence.feature_code;
    END IF;
END;
$projectpulse070_remove_feature$;

DELETE FROM app_permissions permission
USING projectpulse070_permissions_to_remove evidence
WHERE permission.app_permission_id = evidence.app_permission_id
  AND NOT EXISTS (
      SELECT 1 FROM app_role_permissions relationship
      WHERE relationship.app_permission_id = permission.app_permission_id
  )
  AND NOT EXISTS (
      SELECT 1 FROM app_feature_catalog feature
      WHERE feature.required_permission_code = permission.permission_code
  );

-- Keep a migration-created policy when a durable Module 065 source event uses
-- it. That event and its dispatch history are compliance evidence.
DO $projectpulse070_remove_notification_policies$
BEGIN
    IF to_regclass('public.enterprise_notification_policies') IS NOT NULL THEN
        DELETE FROM enterprise_notification_policies policy
        USING projectpulse070_policies_to_remove evidence
        WHERE policy.policy_code = evidence.policy_code
          AND NOT EXISTS (
              SELECT 1
              FROM enterprise_notification_events event
              WHERE event.policy_code = policy.policy_code
          )
          AND NOT EXISTS (
              SELECT 1
              FROM enterprise_notification_policy_audit audit
              WHERE audit.enterprise_notification_policy_id = policy.enterprise_notification_policy_id
          );
    END IF;
END;
$projectpulse070_remove_notification_policies$;

DO $projectpulse070_remove_ai_route$
BEGIN
    IF to_regclass('public.ai_capability_routes') IS NOT NULL THEN
        DELETE FROM ai_capability_routes route
        USING projectpulse070_routes_to_remove evidence
        WHERE route.feature_code = evidence.feature_code
          AND route.revision = 1
          AND route.route_targets = '["celar_ai","claude","openai","local_template"]'::JSONB
          AND route.external_context_policy = 'sanitized_generic_only'
          AND route.updated_by IS NULL
          AND NOT EXISTS (
              SELECT 1
              FROM ai_capability_route_audit audit
              WHERE audit.feature_code = route.feature_code
          );
    END IF;
END;
$projectpulse070_remove_ai_route$;

-- Table drops remove the table-owned triggers. Functions are removed after all
-- dependent tables, making re-running this rollback safe even after a partial run.
DROP TABLE IF EXISTS project_forge_task_details;
DROP TABLE IF EXISTS project_forge_task_dependencies;
DROP TABLE IF EXISTS project_forge_plan_assignments;
DROP TABLE IF EXISTS project_forge_plan_tasks;
DROP TABLE IF EXISTS project_forge_plans;
DROP TABLE IF EXISTS project_forge_audit_events;

DROP FUNCTION IF EXISTS projectpulse070_block_audit_mutation();
DROP FUNCTION IF EXISTS projectpulse070_record_audit_event();
DROP FUNCTION IF EXISTS projectpulse070_validate_task_detail();
DROP FUNCTION IF EXISTS projectpulse070_validate_dependency();
DROP FUNCTION IF EXISTS projectpulse070_validate_plan_assignment();
DROP FUNCTION IF EXISTS projectpulse070_validate_plan_task();
DROP FUNCTION IF EXISTS projectpulse070_touch_revision();

DELETE FROM schema_migrations
WHERE migration_id = '070_module_033_project_forge';

COMMIT;
