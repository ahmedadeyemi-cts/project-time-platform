CREATE EXTENSION IF NOT EXISTS pgcrypto;

ALTER TABLE projects ADD COLUMN IF NOT EXISTS sell_quote_number text NOT NULL DEFAULT '';
ALTER TABLE projects ADD COLUMN IF NOT EXISTS salesforce_id_number text NOT NULL DEFAULT '';
ALTER TABLE projects ADD COLUMN IF NOT EXISTS certinia_id_number text NOT NULL DEFAULT '';

ALTER TABLE work_register_project_metadata ADD COLUMN IF NOT EXISTS sell_quote_number text NOT NULL DEFAULT '';
ALTER TABLE work_register_project_metadata ADD COLUMN IF NOT EXISTS salesforce_id_number text NOT NULL DEFAULT '';
ALTER TABLE work_register_project_metadata ADD COLUMN IF NOT EXISTS certinia_id_number text NOT NULL DEFAULT '';

DO $$
BEGIN
    IF to_regclass('public.work_register_intake_packages') IS NOT NULL THEN
        ALTER TABLE work_register_intake_packages ADD COLUMN IF NOT EXISTS sell_quote_number text NOT NULL DEFAULT '';
        ALTER TABLE work_register_intake_packages ADD COLUMN IF NOT EXISTS salesforce_id_number text NOT NULL DEFAULT '';
        ALTER TABLE work_register_intake_packages ADD COLUMN IF NOT EXISTS certinia_id_number text NOT NULL DEFAULT '';
    END IF;
END $$;

-- Normalize aliases for records where the metadata table already has values.
UPDATE projects p
SET sell_quote_number = COALESCE(NULLIF(p.sell_quote_number, ''), NULLIF(m.sell_quote_number, ''), ''),
    salesforce_id_number = COALESCE(NULLIF(p.salesforce_id_number, ''), NULLIF(m.salesforce_id_number, ''), ''),
    certinia_id_number = COALESCE(NULLIF(p.certinia_id_number, ''), NULLIF(m.certinia_id_number, ''), ''),
    updated_at = NOW()
FROM work_register_project_metadata m
WHERE m.project_id = p.project_id
  AND (
      COALESCE(NULLIF(p.sell_quote_number, ''), NULLIF(m.sell_quote_number, ''), '') <> COALESCE(p.sell_quote_number, '')
      OR COALESCE(NULLIF(p.salesforce_id_number, ''), NULLIF(m.salesforce_id_number, ''), '') <> COALESCE(p.salesforce_id_number, '')
      OR COALESCE(NULLIF(p.certinia_id_number, ''), NULLIF(m.certinia_id_number, ''), '') <> COALESCE(p.certinia_id_number, '')
  );
