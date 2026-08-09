-- Guarded rollback for Pulse migration 082 / Module 083 Full Future Loop.
-- Persistent work items and immutable evidence prevent destructive removal.

BEGIN;

DO $pulse082_rollback_guard$
BEGIN
    IF to_regclass('public.full_future_loop_items') IS NOT NULL
       AND EXISTS(SELECT 1 FROM full_future_loop_items) THEN
        RAISE EXCEPTION 'Rollback refused: Module 083 Full Future Loop work items exist.';
    END IF;
    IF to_regclass('public.full_future_loop_events') IS NOT NULL
       AND EXISTS(SELECT 1 FROM full_future_loop_events) THEN
        RAISE EXCEPTION 'Rollback refused: Module 083 immutable lifecycle events exist.';
    END IF;
    IF to_regclass('public.full_future_loop_artifacts') IS NOT NULL
       AND EXISTS(SELECT 1 FROM full_future_loop_artifacts) THEN
        RAISE EXCEPTION 'Rollback refused: Module 083 immutable evidence artifacts exist.';
    END IF;
END;
$pulse082_rollback_guard$;

CREATE TEMP TABLE pulse082_permissions_to_remove(
    app_permission_id UUID PRIMARY KEY
) ON COMMIT DROP;
CREATE TEMP TABLE pulse082_grants_to_remove(
    app_role_id UUID,
    app_permission_id UUID,
    PRIMARY KEY(app_role_id,app_permission_id)
) ON COMMIT DROP;

INSERT INTO pulse082_permissions_to_remove
SELECT app_permission_id FROM full_future_loop_082_permissions_created
ON CONFLICT DO NOTHING;
INSERT INTO pulse082_grants_to_remove
SELECT app_role_id,app_permission_id FROM full_future_loop_082_role_grants
ON CONFLICT DO NOTHING;

DELETE FROM app_feature_catalog
WHERE feature_code='FULL_FUTURE_LOOP_083' AND module_code='083';

DROP TABLE full_future_loop_082_role_grants;
DROP TABLE full_future_loop_082_permissions_created;

DELETE FROM app_role_permissions grant_row
USING pulse082_grants_to_remove evidence
WHERE grant_row.app_role_id=evidence.app_role_id
  AND grant_row.app_permission_id=evidence.app_permission_id;

DELETE FROM app_permissions permission
USING pulse082_permissions_to_remove evidence
WHERE permission.app_permission_id=evidence.app_permission_id
  AND NOT EXISTS(
      SELECT 1 FROM app_role_permissions remaining
      WHERE remaining.app_permission_id=permission.app_permission_id)
  AND NOT EXISTS(
      SELECT 1 FROM app_feature_catalog feature
      WHERE feature.required_permission_code=permission.permission_code);

DROP TRIGGER IF EXISTS trg_full_future_loop_artifacts_immutable_082 ON full_future_loop_artifacts;
DROP TRIGGER IF EXISTS trg_full_future_loop_events_immutable_082 ON full_future_loop_events;
DROP TRIGGER IF EXISTS trg_full_future_loop_item_touch_082 ON full_future_loop_items;
DROP FUNCTION IF EXISTS pulse082_immutable_full_future_loop_evidence();
DROP FUNCTION IF EXISTS pulse082_touch_full_future_loop_item();

DROP TABLE IF EXISTS full_future_loop_artifacts;
DROP TABLE IF EXISTS full_future_loop_events;
DROP TABLE IF EXISTS full_future_loop_items;

DELETE FROM schema_migrations WHERE migration_id='082_module_083_full_future_loop';

COMMIT;
