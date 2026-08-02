-- ProjectPulse rollback 064
-- Removes only Module 065 enterprise-orchestration schema and RBAC relationships
-- recorded as migration-created. Existing Module 032 dispatch evidence is retained.

BEGIN;

CREATE TEMP TABLE projectpulse064_permissions_to_remove (
    app_permission_id UUID PRIMARY KEY
) ON COMMIT DROP;

CREATE TEMP TABLE projectpulse064_features_to_remove (
    feature_code VARCHAR(160) PRIMARY KEY
) ON COMMIT DROP;

DO $projectpulse064_capture_evidence$
BEGIN
    IF to_regclass('public.enterprise_notification_064_permissions_created') IS NOT NULL THEN
        INSERT INTO projectpulse064_permissions_to_remove(app_permission_id)
        SELECT app_permission_id
        FROM enterprise_notification_064_permissions_created
        ON CONFLICT DO NOTHING;
    END IF;

    IF to_regclass('public.enterprise_notification_064_feature_changes') IS NOT NULL THEN
        INSERT INTO projectpulse064_features_to_remove(feature_code)
        SELECT feature_code
        FROM enterprise_notification_064_feature_changes
        WHERE created_by_migration = TRUE
        ON CONFLICT DO NOTHING;
    END IF;
END;
$projectpulse064_capture_evidence$;

-- Remove only role/permission relationships inserted by migration 064.
DO $projectpulse064_remove_role_grants$
BEGIN
    IF to_regclass('public.enterprise_notification_064_role_grants') IS NOT NULL THEN
        DELETE FROM app_role_permissions relationship
        USING enterprise_notification_064_role_grants evidence
        WHERE relationship.app_role_id = evidence.app_role_id
          AND relationship.app_permission_id = evidence.app_permission_id;
    END IF;
END;
$projectpulse064_remove_role_grants$;

DROP VIEW IF EXISTS enterprise_notification_inventory;

DROP TABLE IF EXISTS enterprise_notification_064_role_grants;
DROP TABLE IF EXISTS enterprise_notification_064_permissions_created;
DROP TABLE IF EXISTS enterprise_notification_064_feature_changes;

DELETE FROM app_permissions permission
USING projectpulse064_permissions_to_remove evidence
WHERE permission.app_permission_id = evidence.app_permission_id
  AND NOT EXISTS (
      SELECT 1
      FROM app_role_permissions relationship
      WHERE relationship.app_permission_id = permission.app_permission_id
  );

DO $projectpulse064_remove_feature$
BEGIN
    IF to_regclass('public.app_feature_catalog') IS NOT NULL THEN
        DELETE FROM app_feature_catalog feature
        USING projectpulse064_features_to_remove evidence
        WHERE feature.feature_code = evidence.feature_code;
    END IF;
END;
$projectpulse064_remove_feature$;

-- Runtime-created project_notification_dispatches and delivery attempts remain as
-- durable evidence. The foreign key below uses ON DELETE SET NULL, so removing the
-- orchestration tables does not delete or rewrite that historical delivery record.
DROP TABLE IF EXISTS enterprise_notification_policy_audit;
DROP TABLE IF EXISTS enterprise_notification_run_history;
DROP TABLE IF EXISTS enterprise_notification_source_checkpoints;
DROP TABLE IF EXISTS enterprise_notification_acknowledgements;
DROP TABLE IF EXISTS enterprise_notification_event_history;
DROP TABLE IF EXISTS enterprise_notification_events;
DROP TABLE IF EXISTS enterprise_notification_policies;

DROP FUNCTION IF EXISTS projectpulse064_block_enterprise_notification_evidence_mutation();

DELETE FROM schema_migrations
WHERE migration_id = '064_module_065_enterprise_notification_orchestration';

COMMIT;