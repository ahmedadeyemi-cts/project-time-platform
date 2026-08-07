-- Pulse migration 076
-- Module 081 Lab Equipment Tracker and location/pod-aware IP address management.
-- Source workbooks are never embedded in source control. Reviewed imports retain
-- checksums, row fingerprints, parser evidence, and redacted validation results.

BEGIN;

DO $pulse076_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.projects') IS NULL
       OR to_regclass('public.app_users') IS NULL
       OR to_regclass('public.app_roles') IS NULL
       OR to_regclass('public.app_permissions') IS NULL
       OR to_regclass('public.app_role_permissions') IS NULL
       OR to_regclass('public.app_feature_catalog') IS NULL THEN
        RAISE EXCEPTION 'Migration 076 requires canonical project, identity, RBAC, and feature-catalog foundations.';
    END IF;
END;
$pulse076_prerequisites$;

CREATE SEQUENCE IF NOT EXISTS lab_equipment_number_seq START 1000;

CREATE TABLE IF NOT EXISTS lab_equipment (
    equipment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    equipment_number VARCHAR(30) NOT NULL UNIQUE DEFAULT ('LAB-' || nextval('lab_equipment_number_seq')::TEXT),
    managing_team VARCHAR(160) NOT NULL,
    equipment_name VARCHAR(240) NOT NULL CHECK (length(btrim(equipment_name)) >= 2),
    equipment_type VARCHAR(100) NOT NULL,
    manufacturer VARCHAR(120) NOT NULL DEFAULT '',
    model VARCHAR(160) NOT NULL DEFAULT '',
    serial_number VARCHAR(180) NOT NULL DEFAULT '',
    asset_tag VARCHAR(120) NOT NULL DEFAULT '',
    hostname VARCHAR(255) NOT NULL DEFAULT '',
    mac_address MACADDR NULL,
    lab_location VARCHAR(180) NOT NULL,
    pod VARCHAR(120) NOT NULL DEFAULT '',
    physical_location VARCHAR(240) NOT NULL DEFAULT '',
    rack VARCHAR(120) NOT NULL DEFAULT '',
    rack_unit_start SMALLINT NULL CHECK (rack_unit_start IS NULL OR rack_unit_start BETWEEN 1 AND 42),
    rack_unit_height SMALLINT NOT NULL DEFAULT 1 CHECK (rack_unit_height BETWEEN 1 AND 42),
    equipment_status VARCHAR(24) NOT NULL DEFAULT 'active' CHECK (equipment_status IN (
        'active','spare','reserved','maintenance','retired','disposed'
    )),
    custodian_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    linked_project_id UUID NULL REFERENCES projects(project_id) ON DELETE SET NULL,
    support_contract VARCHAR(240) NOT NULL DEFAULT '',
    warranty_expires_on DATE NULL,
    notes TEXT NOT NULL DEFAULT '',
    source_workbook VARCHAR(240) NOT NULL DEFAULT '',
    source_sheet VARCHAR(160) NOT NULL DEFAULT '',
    source_row VARCHAR(80) NOT NULL DEFAULT '',
    source_checksum CHAR(64) NOT NULL DEFAULT '',
    import_batch_id UUID NULL,
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    retired_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    retired_at TIMESTAMPTZ NULL,
    revision_number INTEGER NOT NULL DEFAULT 1 CHECK (revision_number >= 1),
    CONSTRAINT ck_lab_equipment_retirement CHECK (
        (equipment_status NOT IN ('retired','disposed') AND retired_at IS NULL AND retired_by_user_id IS NULL)
        OR (equipment_status IN ('retired','disposed') AND retired_at IS NOT NULL AND retired_by_user_id IS NOT NULL)
    )
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_lab_equipment_serial
    ON lab_equipment(lower(serial_number)) WHERE btrim(serial_number)<>'' AND equipment_status<>'disposed';
CREATE UNIQUE INDEX IF NOT EXISTS ux_lab_equipment_asset_tag
    ON lab_equipment(lower(asset_tag)) WHERE btrim(asset_tag)<>'' AND equipment_status<>'disposed';
CREATE INDEX IF NOT EXISTS ix_lab_equipment_scope
    ON lab_equipment(lower(managing_team),lower(lab_location),lower(pod),equipment_status);
CREATE INDEX IF NOT EXISTS ix_lab_equipment_rack
    ON lab_equipment(lower(lab_location),lower(rack),rack_unit_start) WHERE btrim(rack)<>'';

CREATE TABLE IF NOT EXISTS lab_ip_allocations (
    ip_allocation_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    managing_team VARCHAR(160) NOT NULL,
    lab_location VARCHAR(180) NOT NULL,
    pod VARCHAR(120) NOT NULL,
    network_zone VARCHAR(24) NOT NULL CHECK (network_zone IN (
        'underlay','overlay','management','service','transit','other'
    )),
    address_family SMALLINT NOT NULL CHECK (address_family IN (4,6)),
    network_cidr CIDR NOT NULL,
    usable_range VARCHAR(160) NOT NULL DEFAULT '',
    ip_address INET NULL,
    prefix_length SMALLINT NOT NULL CHECK (
        (address_family = 4 AND prefix_length BETWEEN 0 AND 32)
        OR (address_family = 6 AND prefix_length BETWEEN 0 AND 128)
    ),
    gateway INET NULL,
    vlan_id INTEGER NULL CHECK (vlan_id IS NULL OR vlan_id BETWEEN 1 AND 4094),
    vlan_name VARCHAR(120) NOT NULL DEFAULT '',
    vrf VARCHAR(120) NOT NULL DEFAULT '',
    allocation_status VARCHAR(24) NOT NULL DEFAULT 'available' CHECK (allocation_status IN (
        'available','reserved','assigned','conflict','retired'
    )),
    equipment_id UUID NULL REFERENCES lab_equipment(equipment_id) ON DELETE SET NULL,
    interface_name VARCHAR(160) NOT NULL DEFAULT '',
    hostname VARCHAR(255) NOT NULL DEFAULT '',
    purpose TEXT NOT NULL DEFAULT '',
    reservation_owner_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    reservation_expires_at TIMESTAMPTZ NULL,
    source_workbook VARCHAR(240) NOT NULL DEFAULT '',
    source_sheet VARCHAR(160) NOT NULL DEFAULT '',
    source_row VARCHAR(80) NOT NULL DEFAULT '',
    source_checksum CHAR(64) NOT NULL DEFAULT '',
    import_batch_id UUID NULL,
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    revision_number INTEGER NOT NULL DEFAULT 1 CHECK (revision_number >= 1),
    CONSTRAINT ck_lab_ip_family CHECK (
        (address_family=4 AND family(network_cidr)=4 AND (ip_address IS NULL OR family(ip_address)=4))
        OR (address_family=6 AND family(network_cidr)=6 AND (ip_address IS NULL OR family(ip_address)=6))
    ),
    CONSTRAINT ck_lab_ip_assignment CHECK (
        allocation_status NOT IN ('assigned') OR equipment_id IS NOT NULL
    )
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_lab_ip_active_address
    ON lab_ip_allocations(lower(lab_location),lower(pod),network_zone,lower(vrf),ip_address)
    WHERE ip_address IS NOT NULL AND allocation_status<>'retired';
CREATE INDEX IF NOT EXISTS ix_lab_ip_scope
    ON lab_ip_allocations(lower(managing_team),lower(lab_location),lower(pod),network_zone,allocation_status);
CREATE INDEX IF NOT EXISTS ix_lab_ip_equipment
    ON lab_ip_allocations(equipment_id) WHERE equipment_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS lab_cable_connections (
    connection_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    lab_location VARCHAR(180) NOT NULL,
    pod VARCHAR(120) NOT NULL DEFAULT '',
    from_equipment_id UUID NOT NULL REFERENCES lab_equipment(equipment_id) ON DELETE RESTRICT,
    from_interface VARCHAR(160) NOT NULL,
    to_equipment_id UUID NOT NULL REFERENCES lab_equipment(equipment_id) ON DELETE RESTRICT,
    to_interface VARCHAR(160) NOT NULL,
    media_type VARCHAR(80) NOT NULL DEFAULT '',
    cable_label VARCHAR(120) NOT NULL DEFAULT '',
    vlan_id INTEGER NULL CHECK (vlan_id IS NULL OR vlan_id BETWEEN 1 AND 4094),
    ip_address INET NULL,
    connection_status VARCHAR(24) NOT NULL DEFAULT 'active' CHECK (connection_status IN (
        'planned','active','maintenance','disconnected','retired'
    )),
    notes TEXT NOT NULL DEFAULT '',
    source_checksum CHAR(64) NOT NULL DEFAULT '',
    import_batch_id UUID NULL,
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    revision_number INTEGER NOT NULL DEFAULT 1 CHECK (revision_number >= 1),
    CONSTRAINT ck_lab_connection_not_self CHECK (
        from_equipment_id<>to_equipment_id OR lower(from_interface)<>lower(to_interface)
    ),
    CONSTRAINT uq_lab_connection_endpoints UNIQUE (
        from_equipment_id,from_interface,to_equipment_id,to_interface
    )
);

CREATE TABLE IF NOT EXISTS lab_rack_reservations (
    rack_reservation_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    lab_location VARCHAR(180) NOT NULL,
    rack VARCHAR(120) NOT NULL,
    rack_unit_start SMALLINT NOT NULL CHECK (rack_unit_start BETWEEN 1 AND 42),
    rack_unit_height SMALLINT NOT NULL DEFAULT 1 CHECK (rack_unit_height BETWEEN 1 AND 42),
    reservation_status VARCHAR(20) NOT NULL DEFAULT 'reserved' CHECK (reservation_status IN ('reserved','released')),
    reserved_for VARCHAR(240) NOT NULL,
    expires_at TIMESTAMPTZ NULL,
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    released_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    released_at TIMESTAMPTZ NULL
);

CREATE INDEX IF NOT EXISTS ix_lab_rack_reservations_scope
    ON lab_rack_reservations(lower(lab_location),lower(rack),rack_unit_start)
    WHERE reservation_status='reserved';

CREATE TABLE IF NOT EXISTS lab_import_batches (
    import_batch_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    original_file_name VARCHAR(240) NOT NULL,
    file_sha256 CHAR(64) NOT NULL,
    file_size_bytes BIGINT NOT NULL CHECK (file_size_bytes >= 0),
    parser_version VARCHAR(40) NOT NULL,
    source_document_type VARCHAR(40) NOT NULL CHECK (source_document_type IN ('csv','xlsx')),
    target_surface VARCHAR(40) NOT NULL CHECK (target_surface IN ('equipment','ipam','connections')),
    batch_status VARCHAR(24) NOT NULL DEFAULT 'preview' CHECK (batch_status IN (
        'preview','review_required','approved','committed','cancelled','rejected'
    )),
    accepted_count INTEGER NOT NULL DEFAULT 0,
    warning_count INTEGER NOT NULL DEFAULT 0,
    rejected_count INTEGER NOT NULL DEFAULT 0,
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    reviewed_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    committed_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    reviewed_at TIMESTAMPTZ NULL,
    committed_at TIMESTAMPTZ NULL,
    UNIQUE(file_sha256,target_surface)
);

CREATE TABLE IF NOT EXISTS lab_import_rows (
    import_row_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    import_batch_id UUID NOT NULL REFERENCES lab_import_batches(import_batch_id) ON DELETE CASCADE,
    source_sheet VARCHAR(160) NOT NULL DEFAULT '',
    source_row_number INTEGER NOT NULL CHECK (source_row_number >= 1),
    row_fingerprint CHAR(64) NOT NULL,
    row_status VARCHAR(24) NOT NULL CHECK (row_status IN (
        'accepted','warning','duplicate','unresolved','review_required','rejected','committed'
    )),
    sanitized_payload JSONB NOT NULL CHECK (jsonb_typeof(sanitized_payload)='object'),
    validation_messages JSONB NOT NULL DEFAULT '[]'::JSONB CHECK (jsonb_typeof(validation_messages)='array'),
    committed_entity_id UUID NULL,
    reviewed_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    reviewed_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(import_batch_id,row_fingerprint)
);

CREATE INDEX IF NOT EXISTS ix_lab_import_rows_batch_status
    ON lab_import_rows(import_batch_id,row_status,source_row_number);

ALTER TABLE lab_equipment
    DROP CONSTRAINT IF EXISTS fk_lab_equipment_import_batch;
ALTER TABLE lab_equipment
    ADD CONSTRAINT fk_lab_equipment_import_batch FOREIGN KEY(import_batch_id)
    REFERENCES lab_import_batches(import_batch_id) ON DELETE SET NULL;
ALTER TABLE lab_ip_allocations
    DROP CONSTRAINT IF EXISTS fk_lab_ip_import_batch;
ALTER TABLE lab_ip_allocations
    ADD CONSTRAINT fk_lab_ip_import_batch FOREIGN KEY(import_batch_id)
    REFERENCES lab_import_batches(import_batch_id) ON DELETE SET NULL;
ALTER TABLE lab_cable_connections
    DROP CONSTRAINT IF EXISTS fk_lab_connection_import_batch;
ALTER TABLE lab_cable_connections
    ADD CONSTRAINT fk_lab_connection_import_batch FOREIGN KEY(import_batch_id)
    REFERENCES lab_import_batches(import_batch_id) ON DELETE SET NULL;

CREATE TABLE IF NOT EXISTS lab_equipment_audit_events (
    audit_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    entity_type VARCHAR(40) NOT NULL,
    entity_id UUID NOT NULL,
    event_code VARCHAR(80) NOT NULL,
    actual_actor_user_id UUID NOT NULL,
    effective_actor_user_id UUID NOT NULL,
    prior_state JSONB NULL,
    new_state JSONB NULL,
    event_metadata JSONB NOT NULL DEFAULT '{}'::JSONB CHECK (jsonb_typeof(event_metadata)='object'),
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_lab_equipment_audit_entity
    ON lab_equipment_audit_events(entity_type,entity_id,occurred_at DESC);

CREATE OR REPLACE FUNCTION pulse076_touch_revision()
RETURNS TRIGGER LANGUAGE plpgsql AS $pulse076_touch$
BEGIN
    NEW.updated_at:=NOW();
    NEW.revision_number:=OLD.revision_number+1;
    RETURN NEW;
END;
$pulse076_touch$;

CREATE OR REPLACE FUNCTION pulse076_immutable_evidence()
RETURNS TRIGGER LANGUAGE plpgsql AS $pulse076_immutable$
BEGIN
    RAISE EXCEPTION 'Module 081 import and audit evidence is immutable.';
END;
$pulse076_immutable$;

CREATE OR REPLACE FUNCTION pulse076_validate_network()
RETURNS TRIGGER LANGUAGE plpgsql AS $pulse076_network$
BEGIN
    IF NEW.ip_address IS NOT NULL AND NOT (NEW.ip_address <<= NEW.network_cidr) THEN
        RAISE EXCEPTION 'IP address must belong to the selected network.';
    END IF;
    IF NEW.gateway IS NOT NULL AND NOT (NEW.gateway <<= NEW.network_cidr) THEN
        RAISE EXCEPTION 'Gateway must belong to the selected network.';
    END IF;
    IF EXISTS (
        SELECT 1 FROM lab_ip_allocations existing
        WHERE existing.ip_allocation_id<>NEW.ip_allocation_id
          AND existing.allocation_status<>'retired'
          AND NEW.allocation_status<>'retired'
          AND lower(existing.lab_location)=lower(NEW.lab_location)
          AND lower(existing.pod)=lower(NEW.pod)
          AND existing.network_zone=NEW.network_zone
          AND lower(existing.vrf)=lower(NEW.vrf)
          AND existing.network_cidr && NEW.network_cidr
          AND existing.network_cidr<>NEW.network_cidr
    ) THEN
        RAISE EXCEPTION 'Overlapping network exists in the same location, pod, and zone.';
    END IF;
    RETURN NEW;
END;
$pulse076_network$;

CREATE OR REPLACE FUNCTION pulse076_validate_rack()
RETURNS TRIGGER LANGUAGE plpgsql AS $pulse076_rack$
BEGIN
    IF NEW.rack_unit_start IS NOT NULL AND NEW.rack_unit_start+NEW.rack_unit_height-1>42 THEN
        RAISE EXCEPTION 'Rack placement exceeds the supported rack-unit range.';
    END IF;
    IF NEW.rack_unit_start IS NOT NULL AND NEW.equipment_status NOT IN ('retired','disposed') AND EXISTS (
        SELECT 1 FROM lab_equipment existing
        WHERE existing.equipment_id<>NEW.equipment_id
          AND existing.equipment_status NOT IN ('retired','disposed')
          AND lower(existing.lab_location)=lower(NEW.lab_location)
          AND lower(existing.rack)=lower(NEW.rack)
          AND existing.rack_unit_start IS NOT NULL
          AND int4range(existing.rack_unit_start,existing.rack_unit_start+existing.rack_unit_height,'[)')
              && int4range(NEW.rack_unit_start,NEW.rack_unit_start+NEW.rack_unit_height,'[)')
    ) THEN
        RAISE EXCEPTION 'Rack-unit placement conflicts with active equipment.';
    END IF;
    RETURN NEW;
END;
$pulse076_rack$;

DROP TRIGGER IF EXISTS trg_lab_equipment_touch_076 ON lab_equipment;
CREATE TRIGGER trg_lab_equipment_touch_076 BEFORE UPDATE ON lab_equipment
FOR EACH ROW EXECUTE FUNCTION pulse076_touch_revision();
DROP TRIGGER IF EXISTS trg_lab_ip_touch_076 ON lab_ip_allocations;
CREATE TRIGGER trg_lab_ip_touch_076 BEFORE UPDATE ON lab_ip_allocations
FOR EACH ROW EXECUTE FUNCTION pulse076_touch_revision();
DROP TRIGGER IF EXISTS trg_lab_connection_touch_076 ON lab_cable_connections;
CREATE TRIGGER trg_lab_connection_touch_076 BEFORE UPDATE ON lab_cable_connections
FOR EACH ROW EXECUTE FUNCTION pulse076_touch_revision();
DROP TRIGGER IF EXISTS trg_lab_ip_validate_076 ON lab_ip_allocations;
CREATE TRIGGER trg_lab_ip_validate_076 BEFORE INSERT OR UPDATE ON lab_ip_allocations
FOR EACH ROW EXECUTE FUNCTION pulse076_validate_network();
DROP TRIGGER IF EXISTS trg_lab_rack_validate_076 ON lab_equipment;
CREATE TRIGGER trg_lab_rack_validate_076 BEFORE INSERT OR UPDATE ON lab_equipment
FOR EACH ROW EXECUTE FUNCTION pulse076_validate_rack();
DROP TRIGGER IF EXISTS trg_lab_audit_immutable_076 ON lab_equipment_audit_events;
CREATE TRIGGER trg_lab_audit_immutable_076 BEFORE UPDATE OR DELETE ON lab_equipment_audit_events
FOR EACH ROW EXECUTE FUNCTION pulse076_immutable_evidence();

CREATE TABLE IF NOT EXISTS lab_equipment_076_permissions_created(
    app_permission_id UUID PRIMARY KEY REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    permission_code VARCHAR(100) NOT NULL UNIQUE,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE IF NOT EXISTS lab_equipment_076_role_grants(
    app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE RESTRICT,
    app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY(app_role_id,app_permission_id)
);

WITH inserted AS (
    INSERT INTO app_permissions(permission_code,permission_name,module_code,permission_description)
    VALUES
      ('VIEW_LAB_EQUIPMENT_081','View Lab Equipment Tracker','081','View equipment, IPAM, cabling, rack, import, and audit evidence within authorized team/project scope.'),
      ('MANAGE_LAB_EQUIPMENT_081','Manage Lab Equipment Tracker','081','Create and update equipment, IP allocations, cabling, and rack records within authorized scope.'),
      ('IMPORT_LAB_EQUIPMENT_081','Import Lab Equipment Data','081','Preview, validate, review, and commit approved lab workbook or CSV data.'),
      ('EXPORT_LAB_EQUIPMENT_081','Export Lab Equipment Data','081','Create US Signal-branded role-scoped Excel and PDF evidence artifacts.')
    ON CONFLICT(permission_code) DO NOTHING
    RETURNING app_permission_id,permission_code
)
INSERT INTO lab_equipment_076_permissions_created(app_permission_id,permission_code)
SELECT app_permission_id,permission_code FROM inserted ON CONFLICT DO NOTHING;

WITH desired(role_code,permission_code) AS (
    VALUES
      ('SUPER_ADMINISTRATOR','VIEW_LAB_EQUIPMENT_081'),('SUPER_ADMINISTRATOR','MANAGE_LAB_EQUIPMENT_081'),('SUPER_ADMINISTRATOR','IMPORT_LAB_EQUIPMENT_081'),('SUPER_ADMINISTRATOR','EXPORT_LAB_EQUIPMENT_081'),
      ('ADMINISTRATOR','VIEW_LAB_EQUIPMENT_081'),('ADMINISTRATOR','MANAGE_LAB_EQUIPMENT_081'),('ADMINISTRATOR','IMPORT_LAB_EQUIPMENT_081'),('ADMINISTRATOR','EXPORT_LAB_EQUIPMENT_081'),
      ('PROJECT_TEAM_COORDINATOR','VIEW_LAB_EQUIPMENT_081'),('PROJECT_TEAM_COORDINATOR','MANAGE_LAB_EQUIPMENT_081'),('PROJECT_TEAM_COORDINATOR','IMPORT_LAB_EQUIPMENT_081'),('PROJECT_TEAM_COORDINATOR','EXPORT_LAB_EQUIPMENT_081'),
      ('MANAGER','VIEW_LAB_EQUIPMENT_081'),('MANAGER','MANAGE_LAB_EQUIPMENT_081'),('MANAGER','EXPORT_LAB_EQUIPMENT_081'),
      ('ENGINEERING_MANAGER','VIEW_LAB_EQUIPMENT_081'),('ENGINEERING_MANAGER','MANAGE_LAB_EQUIPMENT_081'),('ENGINEERING_MANAGER','EXPORT_LAB_EQUIPMENT_081'),
      ('ENGINEERING_TEAM_LEAD','VIEW_LAB_EQUIPMENT_081'),('ENGINEERING_TEAM_LEAD','MANAGE_LAB_EQUIPMENT_081'),('ENGINEERING_TEAM_LEAD','EXPORT_LAB_EQUIPMENT_081'),
      ('ENGINEER','VIEW_LAB_EQUIPMENT_081'),('ENGINEER','MANAGE_LAB_EQUIPMENT_081'),
      ('ENGINEERING','VIEW_LAB_EQUIPMENT_081'),('ENGINEERING','MANAGE_LAB_EQUIPMENT_081'),
      ('SYSTEMS_ENGINEER','VIEW_LAB_EQUIPMENT_081'),('SYSTEMS_ENGINEER','MANAGE_LAB_EQUIPMENT_081'),
      ('NETWORK_ENGINEER','VIEW_LAB_EQUIPMENT_081'),('NETWORK_ENGINEER','MANAGE_LAB_EQUIPMENT_081'),
      ('PROJECT_MANAGER','VIEW_LAB_EQUIPMENT_081'),('PROJECT_MANAGEMENT','VIEW_LAB_EQUIPMENT_081'),
      ('SOLUTION_ARCHITECT','VIEW_LAB_EQUIPMENT_081')
), candidates AS (
    SELECT role.app_role_id,permission.app_permission_id
    FROM desired
    JOIN app_roles role ON upper(role.role_code)=desired.role_code AND role.is_active=TRUE
    JOIN app_permissions permission ON permission.permission_code=desired.permission_code
    LEFT JOIN app_role_permissions existing ON existing.app_role_id=role.app_role_id AND existing.app_permission_id=permission.app_permission_id
    WHERE existing.app_role_permission_id IS NULL
), inserted AS (
    INSERT INTO app_role_permissions(app_role_id,app_permission_id,created_at)
    SELECT app_role_id,app_permission_id,NOW() FROM candidates
    ON CONFLICT(app_role_id,app_permission_id) DO NOTHING
    RETURNING app_role_id,app_permission_id
)
INSERT INTO lab_equipment_076_role_grants(app_role_id,app_permission_id)
SELECT app_role_id,app_permission_id FROM inserted ON CONFLICT DO NOTHING;

INSERT INTO app_feature_catalog(feature_code,feature_name,module_code,route_anchor,required_permission_code,feature_description,display_order,is_active)
VALUES('LAB_EQUIPMENT_TRACKER_081','Lab Equipment Tracker and IP Address Management','081','#lab-equipment-tracker','VIEW_LAB_EQUIPMENT_081','Governed lab equipment, location/pod-aware IPAM, cabling, rack occupancy, reviewed imports, immutable history, and branded exports.',181,TRUE)
ON CONFLICT(feature_code) DO UPDATE SET
  feature_name=EXCLUDED.feature_name,module_code=EXCLUDED.module_code,route_anchor=EXCLUDED.route_anchor,
  required_permission_code=EXCLUDED.required_permission_code,feature_description=EXCLUDED.feature_description,
  is_active=TRUE,updated_at=NOW();

INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES('076_module_081_lab_equipment_tracker','Create governed Module 081 equipment, IPAM, cabling, rack, reviewed import, immutable audit, RBAC, and export foundations',NOW())
ON CONFLICT(migration_id) DO NOTHING;

COMMIT;
