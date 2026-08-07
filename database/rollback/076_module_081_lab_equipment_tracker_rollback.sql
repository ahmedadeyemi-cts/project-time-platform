-- Guarded rollback for Module 081. Operational and immutable evidence prevents removal.

BEGIN;

DO $pulse076_rollback_guard$
BEGIN
    IF to_regclass('public.lab_equipment') IS NOT NULL AND EXISTS(SELECT 1 FROM lab_equipment) THEN
        RAISE EXCEPTION 'Rollback refused: Module 081 equipment records exist.';
    END IF;
    IF to_regclass('public.lab_ip_allocations') IS NOT NULL AND EXISTS(SELECT 1 FROM lab_ip_allocations) THEN
        RAISE EXCEPTION 'Rollback refused: Module 081 IP allocations exist.';
    END IF;
    IF to_regclass('public.lab_import_batches') IS NOT NULL AND EXISTS(SELECT 1 FROM lab_import_batches) THEN
        RAISE EXCEPTION 'Rollback refused: Module 081 import evidence exists.';
    END IF;
    IF to_regclass('public.lab_equipment_audit_events') IS NOT NULL AND EXISTS(SELECT 1 FROM lab_equipment_audit_events) THEN
        RAISE EXCEPTION 'Rollback refused: Module 081 audit evidence exists.';
    END IF;
END;
$pulse076_rollback_guard$;

CREATE TEMP TABLE pulse076_permissions_to_remove(app_permission_id UUID PRIMARY KEY) ON COMMIT DROP;
CREATE TEMP TABLE pulse076_grants_to_remove(app_role_id UUID,app_permission_id UUID,PRIMARY KEY(app_role_id,app_permission_id)) ON COMMIT DROP;
INSERT INTO pulse076_permissions_to_remove SELECT app_permission_id FROM lab_equipment_076_permissions_created ON CONFLICT DO NOTHING;
INSERT INTO pulse076_grants_to_remove SELECT app_role_id,app_permission_id FROM lab_equipment_076_role_grants ON CONFLICT DO NOTHING;

DELETE FROM app_feature_catalog WHERE feature_code='LAB_EQUIPMENT_TRACKER_081';
DROP TABLE lab_equipment_076_role_grants;
DROP TABLE lab_equipment_076_permissions_created;
DELETE FROM app_role_permissions grant_row USING pulse076_grants_to_remove evidence
WHERE grant_row.app_role_id=evidence.app_role_id AND grant_row.app_permission_id=evidence.app_permission_id;
DELETE FROM app_permissions permission USING pulse076_permissions_to_remove evidence
WHERE permission.app_permission_id=evidence.app_permission_id
  AND NOT EXISTS(SELECT 1 FROM app_role_permissions remaining WHERE remaining.app_permission_id=permission.app_permission_id)
  AND NOT EXISTS(SELECT 1 FROM app_feature_catalog feature WHERE feature.required_permission_code=permission.permission_code);

DROP TRIGGER IF EXISTS trg_lab_audit_immutable_076 ON lab_equipment_audit_events;
DROP TRIGGER IF EXISTS trg_lab_rack_validate_076 ON lab_equipment;
DROP TRIGGER IF EXISTS trg_lab_ip_validate_076 ON lab_ip_allocations;
DROP TRIGGER IF EXISTS trg_lab_connection_touch_076 ON lab_cable_connections;
DROP TRIGGER IF EXISTS trg_lab_ip_touch_076 ON lab_ip_allocations;
DROP TRIGGER IF EXISTS trg_lab_equipment_touch_076 ON lab_equipment;
DROP FUNCTION IF EXISTS pulse076_validate_rack();
DROP FUNCTION IF EXISTS pulse076_validate_network();
DROP FUNCTION IF EXISTS pulse076_immutable_evidence();
DROP FUNCTION IF EXISTS pulse076_touch_revision();

DROP TABLE IF EXISTS lab_equipment_audit_events;
DROP TABLE IF EXISTS lab_import_rows;
DROP TABLE IF EXISTS lab_rack_reservations;
DROP TABLE IF EXISTS lab_cable_connections;
DROP TABLE IF EXISTS lab_ip_allocations;
DROP TABLE IF EXISTS lab_equipment;
DROP TABLE IF EXISTS lab_import_batches;
DROP SEQUENCE IF EXISTS lab_equipment_number_seq;
DELETE FROM schema_migrations WHERE migration_id='076_module_081_lab_equipment_tracker';

COMMIT;
