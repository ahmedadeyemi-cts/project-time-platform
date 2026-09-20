-- Temporary coverage and receipt acknowledgement retain immutable handoff metadata.
-- Return dates are operational prompts only; no scheduled owner reassignment exists.
BEGIN;
SELECT pg_advisory_xact_lock(250119);
DO $$ BEGIN
    IF NOT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='116_module025_governed_ownership_transfer') THEN
        RAISE EXCEPTION 'Temporary SOW/GSD handoffs require migration 116';
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS module025_sow_gsd_handoffs (
    handoff_id uuid PRIMARY KEY,
    engagement_id uuid NOT NULL REFERENCES module025_sow_gsd_engagements(engagement_id),
    transfer_event_id bigint NOT NULL UNIQUE REFERENCES module025_sow_gsd_events(event_id),
    previous_owner_user_id uuid NOT NULL REFERENCES app_users(user_id),
    previous_owner_display_name varchar(320) NOT NULL,
    new_owner_user_id uuid NOT NULL REFERENCES app_users(user_id),
    new_owner_display_name varchar(320) NOT NULL,
    handoff_mode varchar(20) NOT NULL CHECK(handoff_mode IN ('permanent','temporary')),
    return_date date NULL,
    return_of_handoff_id uuid NULL REFERENCES module025_sow_gsd_handoffs(handoff_id),
    created_at timestamptz NOT NULL DEFAULT now(),
    CHECK(previous_owner_user_id<>new_owner_user_id),
    CHECK((handoff_mode='temporary')=(return_date IS NOT NULL)),
    CHECK(return_of_handoff_id IS NULL OR handoff_mode='permanent')
);
CREATE INDEX IF NOT EXISTS ix_module025_handoff_engagement ON module025_sow_gsd_handoffs(engagement_id,transfer_event_id DESC);
CREATE UNIQUE INDEX IF NOT EXISTS ux_module025_handoff_return ON module025_sow_gsd_handoffs(return_of_handoff_id) WHERE return_of_handoff_id IS NOT NULL;

-- Handoff records must describe the exact ownership event in this transaction.
CREATE OR REPLACE FUNCTION module025_validate_handoff_metadata()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM module025_sow_gsd_events e
        WHERE e.event_id=NEW.transfer_event_id AND e.engagement_id=NEW.engagement_id
          AND e.event_type='ownership_transferred'
          AND e.evidence_json->>'transferTransactionId'=txid_current()::text
          AND e.evidence_json->>'handoffId'=NEW.handoff_id::text
          AND e.evidence_json->>'previousOwnerUserId'=NEW.previous_owner_user_id::text
          AND e.evidence_json->>'previousOwnerDisplayName'=NEW.previous_owner_display_name
          AND e.evidence_json->>'newOwnerUserId'=NEW.new_owner_user_id::text
          AND e.evidence_json->>'newOwnerDisplayName'=NEW.new_owner_display_name
          AND e.evidence_json->>'mode'=NEW.handoff_mode
          AND e.evidence_json->>'returnDate' IS NOT DISTINCT FROM to_char(NEW.return_date,'YYYY-MM-DD')
          AND e.evidence_json->>'returnOfHandoffId' IS NOT DISTINCT FROM NEW.return_of_handoff_id::text) THEN
        RAISE EXCEPTION 'Handoff metadata requires matching ownership evidence in the same transaction';
    END IF;
    IF NEW.return_of_handoff_id IS NOT NULL AND NOT EXISTS(
        SELECT 1 FROM module025_sow_gsd_handoffs original
        WHERE original.handoff_id=NEW.return_of_handoff_id AND original.engagement_id=NEW.engagement_id
          AND original.handoff_mode='temporary'
          AND original.previous_owner_user_id=NEW.new_owner_user_id
          AND original.new_owner_user_id=NEW.previous_owner_user_id) THEN
        RAISE EXCEPTION 'A return must reference the original temporary coverage and owner pair';
    END IF;
    RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS module025_handoff_metadata_check ON module025_sow_gsd_handoffs;
CREATE TRIGGER module025_handoff_metadata_check BEFORE INSERT ON module025_sow_gsd_handoffs
    FOR EACH ROW EXECUTE FUNCTION module025_validate_handoff_metadata();
DROP TRIGGER IF EXISTS module025_handoff_no_change ON module025_sow_gsd_handoffs;
CREATE TRIGGER module025_handoff_no_change BEFORE UPDATE OR DELETE ON module025_sow_gsd_handoffs
    FOR EACH ROW EXECUTE FUNCTION module025_reject_evidence_mutation();
DROP TRIGGER IF EXISTS module025_handoff_no_truncate ON module025_sow_gsd_handoffs;
CREATE TRIGGER module025_handoff_no_truncate BEFORE TRUNCATE ON module025_sow_gsd_handoffs
    FOR EACH STATEMENT EXECUTE FUNCTION module025_reject_evidence_mutation();

-- Ack and return are append-only events; duplicate receipt cannot create noise.
CREATE UNIQUE INDEX IF NOT EXISTS ux_module025_handoff_ack ON module025_sow_gsd_events(engagement_id,(evidence_json->>'handoffId'))
    WHERE event_type='handoff_acknowledged';
CREATE UNIQUE INDEX IF NOT EXISTS ux_module025_handoff_return_event ON module025_sow_gsd_events(engagement_id,(evidence_json->>'handoffId'))
    WHERE event_type='handoff_returned';

DO $$ BEGIN
    IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='ptp_app') THEN
        GRANT SELECT,INSERT ON module025_sow_gsd_handoffs TO ptp_app;
        REVOKE UPDATE,DELETE,TRUNCATE ON module025_sow_gsd_handoffs FROM ptp_app;
    END IF;
END $$;
INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES('119_module025_temporary_handoffs','Immutable SOW/GSD PTO coverage, explicit safe return and assigned-SA receipt acknowledgement',now())
ON CONFLICT(migration_id) DO NOTHING;
COMMIT;
