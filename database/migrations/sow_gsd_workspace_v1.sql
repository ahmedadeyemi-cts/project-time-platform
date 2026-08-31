BEGIN;

CREATE TABLE IF NOT EXISTS sow_gsd_records (
    id uuid PRIMARY KEY,
    record_number varchar(64) NOT NULL UNIQUE,
    solution_architect_user_id uuid NOT NULL,
    solution_architect_name text NOT NULL DEFAULT '',
    customer_id text NULL,
    customer_name text NOT NULL DEFAULT '',
    customer_is_manual boolean NOT NULL DEFAULT false,
    opportunity_id text NULL,
    project_name text NOT NULL DEFAULT '',
    contract_type varchar(16) NOT NULL DEFAULT 'T&M',
    gsd_template varchar(32) NOT NULL DEFAULT 'Standard',
    account_executive_user_id uuid NULL,
    account_executive_name text NOT NULL DEFAULT '',
    resale_user_id uuid NULL,
    resale_name text NOT NULL DEFAULT '',
    service_overview text NOT NULL DEFAULT '',
    scope_json jsonb NOT NULL DEFAULT '{"phases":[]}'::jsonb,
    document_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    status varchar(16) NOT NULL DEFAULT 'Draft',
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    created_by_user_id uuid NOT NULL,
    updated_at_utc timestamptz NOT NULL DEFAULT now(),
    updated_by_user_id uuid NOT NULL,
    confirmed_at_utc timestamptz NULL,
    confirmed_by_user_id uuid NULL,
    archived_at_utc timestamptz NULL,
    archived_by_user_id uuid NULL,
    CONSTRAINT ck_sow_gsd_contract_type CHECK (contract_type IN ('T&M', 'Fixed')),
    CONSTRAINT ck_sow_gsd_template CHECK (gsd_template IN ('Standard', 'ToyotaHyundai')),
    CONSTRAINT ck_sow_gsd_status CHECK (status IN ('Draft', 'Confirmed', 'Archived'))
);

CREATE INDEX IF NOT EXISTS ix_sow_gsd_records_sa_status_updated
    ON sow_gsd_records (solution_architect_user_id, status, updated_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_sow_gsd_records_customer_name
    ON sow_gsd_records (lower(customer_name));
CREATE INDEX IF NOT EXISTS ix_sow_gsd_records_record_number_lower
    ON sow_gsd_records (lower(record_number));

CREATE OR REPLACE FUNCTION prevent_sow_gsd_record_number_update()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.record_number IS DISTINCT FROM OLD.record_number THEN
        RAISE EXCEPTION 'record_number is immutable';
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_sow_gsd_record_number_immutable ON sow_gsd_records;
CREATE TRIGGER trg_sow_gsd_record_number_immutable
BEFORE UPDATE OF record_number ON sow_gsd_records
FOR EACH ROW
EXECUTE FUNCTION prevent_sow_gsd_record_number_update();

COMMIT;
