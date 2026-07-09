-- 055D.2A - XLSX GSD extraction repair and tightened intake flow

BEGIN;

ALTER TABLE work_register_intake_packages
    ADD COLUMN IF NOT EXISTS customer_id uuid NULL;

ALTER TABLE work_register_intake_packages
    ADD COLUMN IF NOT EXISTS contract_type varchar(80) NOT NULL DEFAULT 'Fixed Price';

CREATE INDEX IF NOT EXISTS idx_work_register_intake_packages_customer
    ON work_register_intake_packages(customer_id);

COMMIT;
