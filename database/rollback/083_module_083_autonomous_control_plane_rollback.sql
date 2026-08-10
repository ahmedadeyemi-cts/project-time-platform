-- Guarded rollback for Module 083 autonomous control-plane migration 083.
-- User-created runs, approvals, manifests, evidence, or outbox records prevent removal.

BEGIN;

DO $pulse083_rollback_guard$
BEGIN
    IF to_regclass('public.full_future_loop_automation_runs') IS NOT NULL
       AND EXISTS(SELECT 1 FROM full_future_loop_automation_runs) THEN
        RAISE EXCEPTION 'Rollback refused: Module 083 autonomous run evidence exists.';
    END IF;
    IF to_regclass('public.full_future_loop_automation_approvals') IS NOT NULL
       AND EXISTS(SELECT 1 FROM full_future_loop_automation_approvals) THEN
        RAISE EXCEPTION 'Rollback refused: Module 083 approval evidence exists.';
    END IF;
    IF to_regclass('public.full_future_loop_release_manifests') IS NOT NULL
       AND EXISTS(SELECT 1 FROM full_future_loop_release_manifests) THEN
        RAISE EXCEPTION 'Rollback refused: Module 083 immutable release manifests exist.';
    END IF;
    IF to_regclass('public.full_future_loop_automation_evidence') IS NOT NULL
       AND EXISTS(SELECT 1 FROM full_future_loop_automation_evidence) THEN
        RAISE EXCEPTION 'Rollback refused: Module 083 append-only automation evidence exists.';
    END IF;
    IF to_regclass('public.full_future_loop_outbox') IS NOT NULL
       AND EXISTS(SELECT 1 FROM full_future_loop_outbox) THEN
        RAISE EXCEPTION 'Rollback refused: Module 083 outbox records exist.';
    END IF;
    IF to_regclass('public.full_future_loop_automation_state') IS NOT NULL
       AND EXISTS(
           SELECT 1 FROM full_future_loop_automation_state
           WHERE automation_enabled=TRUE OR global_kill_switch=FALSE OR dry_run_only=FALSE OR revision_number<>1) THEN
        RAISE EXCEPTION 'Rollback refused: Module 083 runtime state has been changed.';
    END IF;
    IF to_regclass('public.full_future_loop_automation_adapters') IS NOT NULL
       AND EXISTS(
           SELECT 1 FROM full_future_loop_automation_adapters
           WHERE adapter_mode<>'disabled' OR revision_number<>1 OR updated_by_user_id IS NOT NULL) THEN
        RAISE EXCEPTION 'Rollback refused: Module 083 adapter configuration has been changed.';
    END IF;
    IF to_regclass('public.full_future_loop_automation_policies') IS NOT NULL
       AND EXISTS(
           SELECT 1 FROM full_future_loop_automation_policies
           WHERE policy_version_id<>'08300000-0000-0000-0000-000000000001'::UUID
              OR policy_version<>'enterprise-default-v1') THEN
        RAISE EXCEPTION 'Rollback refused: additional Module 083 automation policy versions exist.';
    END IF;
END;
$pulse083_rollback_guard$;

CREATE TEMP TABLE pulse083_permissions_to_remove(app_permission_id UUID PRIMARY KEY) ON COMMIT DROP;
CREATE TEMP TABLE pulse083_grants_to_remove(app_role_id UUID,app_permission_id UUID,PRIMARY KEY(app_role_id,app_permission_id)) ON COMMIT DROP;

INSERT INTO pulse083_permissions_to_remove
SELECT app_permission_id FROM full_future_loop_083_permissions_created
ON CONFLICT DO NOTHING;

INSERT INTO pulse083_grants_to_remove
SELECT app_role_id,app_permission_id FROM full_future_loop_083_role_grants
ON CONFLICT DO NOTHING;

DELETE FROM app_feature_catalog WHERE feature_code='FULL_FUTURE_LOOP_AUTOMATION_083';

DROP TABLE full_future_loop_083_role_grants;
DROP TABLE full_future_loop_083_permissions_created;

DELETE FROM app_role_permissions grant_row
USING pulse083_grants_to_remove evidence
WHERE grant_row.app_role_id=evidence.app_role_id
  AND grant_row.app_permission_id=evidence.app_permission_id;

DELETE FROM app_permissions permission
USING pulse083_permissions_to_remove evidence
WHERE permission.app_permission_id=evidence.app_permission_id
  AND NOT EXISTS(
      SELECT 1 FROM app_role_permissions remaining
      WHERE remaining.app_permission_id=permission.app_permission_id)
  AND NOT EXISTS(
      SELECT 1 FROM app_feature_catalog feature
      WHERE feature.required_permission_code=permission.permission_code);

DROP TRIGGER IF EXISTS trg_full_future_loop_automation_evidence_immutable_083 ON full_future_loop_automation_evidence;
DROP TRIGGER IF EXISTS trg_full_future_loop_release_manifests_immutable_083 ON full_future_loop_release_manifests;
DROP TRIGGER IF EXISTS trg_full_future_loop_automation_policies_immutable_083 ON full_future_loop_automation_policies;
DROP FUNCTION IF EXISTS pulse083_immutable_automation_evidence();

DROP TABLE IF EXISTS full_future_loop_outbox;
DROP TABLE IF EXISTS full_future_loop_automation_evidence;
DROP TABLE IF EXISTS full_future_loop_release_manifests;
DROP TABLE IF EXISTS full_future_loop_automation_approvals;
DROP TABLE IF EXISTS full_future_loop_automation_steps;
DROP TABLE IF EXISTS full_future_loop_automation_runs;
DROP TABLE IF EXISTS full_future_loop_automation_adapters;
DROP TABLE IF EXISTS full_future_loop_automation_state;
DROP TABLE IF EXISTS full_future_loop_automation_policies;

DELETE FROM schema_migrations WHERE migration_id='083_module_083_autonomous_control_plane';

COMMIT;
