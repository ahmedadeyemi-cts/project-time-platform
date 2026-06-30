-- 019M-AG Phase 1: Customer directory and intake cost foundation

CREATE TABLE IF NOT EXISTS client_contacts (
    client_contact_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    client_id uuid NOT NULL REFERENCES clients(client_id) ON DELETE CASCADE,
    contact_name varchar(200) NOT NULL,
    title varchar(160),
    role_description varchar(160),
    email varchar(320),
    phone varchar(80),
    address_line1 text,
    address_line2 text,
    city varchar(120),
    state_region varchar(120),
    postal_code varchar(40),
    country varchar(120) NOT NULL DEFAULT 'United States',
    is_primary boolean NOT NULL DEFAULT false,
    is_active boolean NOT NULL DEFAULT true,
    display_order integer NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_client_contacts_client_id
    ON client_contacts(client_id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_client_contacts_primary_active
    ON client_contacts(client_id)
    WHERE is_primary = true AND is_active = true;

CREATE UNIQUE INDEX IF NOT EXISTS ux_client_contacts_client_email_active
    ON client_contacts(client_id, lower(email))
    WHERE email IS NOT NULL AND is_active = true;

CREATE OR REPLACE FUNCTION projectpulse_enforce_client_contact_limit()
RETURNS trigger AS $$
DECLARE
    active_contact_count integer;
BEGIN
    IF NEW.is_active THEN
        SELECT COUNT(*)
        INTO active_contact_count
        FROM client_contacts
        WHERE client_id = NEW.client_id
          AND is_active = true
          AND client_contact_id <> COALESCE(NEW.client_contact_id, '00000000-0000-0000-0000-000000000000'::uuid);

        IF active_contact_count >= 10 THEN
            RAISE EXCEPTION 'Customer cannot have more than 10 active contacts.';
        END IF;
    END IF;

    NEW.updated_at = now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_projectpulse_enforce_client_contact_limit ON client_contacts;

CREATE TRIGGER trg_projectpulse_enforce_client_contact_limit
BEFORE INSERT OR UPDATE ON client_contacts
FOR EACH ROW
EXECUTE FUNCTION projectpulse_enforce_client_contact_limit();

ALTER TABLE project_intake_requests
    ADD COLUMN IF NOT EXISTS client_id uuid,
    ADD COLUMN IF NOT EXISTS planned_engineering_cost numeric(14,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS planned_pm_cost numeric(14,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS planned_total_project_cost numeric(14,2) NOT NULL DEFAULT 0;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_project_intake_requests_client_id'
    ) THEN
        ALTER TABLE project_intake_requests
            ADD CONSTRAINT fk_project_intake_requests_client_id
            FOREIGN KEY (client_id)
            REFERENCES clients(client_id);
    END IF;
END;
$$;

ALTER TABLE projects
    ADD COLUMN IF NOT EXISTS planned_engineering_cost numeric(14,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS planned_pm_cost numeric(14,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS planned_total_project_cost numeric(14,2) NOT NULL DEFAULT 0;

CREATE OR REPLACE FUNCTION projectpulse_sync_planned_project_total()
RETURNS trigger AS $$
BEGIN
    NEW.planned_total_project_cost = COALESCE(NEW.planned_engineering_cost, 0) + COALESCE(NEW.planned_pm_cost, 0);
    NEW.updated_at = now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_projectpulse_sync_intake_planned_total ON project_intake_requests;

CREATE TRIGGER trg_projectpulse_sync_intake_planned_total
BEFORE INSERT OR UPDATE OF planned_engineering_cost, planned_pm_cost ON project_intake_requests
FOR EACH ROW
EXECUTE FUNCTION projectpulse_sync_planned_project_total();

DROP TRIGGER IF EXISTS trg_projectpulse_sync_project_planned_total ON projects;

CREATE TRIGGER trg_projectpulse_sync_project_planned_total
BEFORE INSERT OR UPDATE OF planned_engineering_cost, planned_pm_cost ON projects
FOR EACH ROW
EXECUTE FUNCTION projectpulse_sync_planned_project_total();

UPDATE project_intake_requests pir
SET client_id = c.client_id
FROM clients c
WHERE pir.client_id IS NULL
  AND lower(trim(pir.client_name)) = lower(trim(c.client_name));

UPDATE project_intake_requests
SET planned_engineering_cost = CASE
        WHEN request_number = 'INTAKE-2026-001' THEN 48000.00
        WHEN request_number = 'INTAKE-2026-002' THEN 63000.00
        ELSE planned_engineering_cost
    END,
    planned_pm_cost = CASE
        WHEN request_number = 'INTAKE-2026-001' THEN 12000.00
        WHEN request_number = 'INTAKE-2026-002' THEN 15000.00
        ELSE planned_pm_cost
    END;

UPDATE projects
SET planned_engineering_cost = CASE
        WHEN project_code = 'GLH-CC-2026' THEN 48000.00
        WHEN project_code = 'USS-PSA-2026' THEN 0.00
        ELSE planned_engineering_cost
    END,
    planned_pm_cost = CASE
        WHEN project_code = 'GLH-CC-2026' THEN 12000.00
        WHEN project_code = 'USS-PSA-2026' THEN 0.00
        ELSE planned_pm_cost
    END;

INSERT INTO client_contacts (
    client_id,
    contact_name,
    title,
    role_description,
    email,
    phone,
    address_line1,
    city,
    state_region,
    postal_code,
    country,
    is_primary,
    display_order
)
SELECT
    c.client_id,
    seed.contact_name,
    seed.title,
    seed.role_description,
    seed.email,
    seed.phone,
    seed.address_line1,
    seed.city,
    seed.state_region,
    seed.postal_code,
    seed.country,
    seed.is_primary,
    seed.display_order
FROM clients c
JOIN (
    VALUES
        ('GLH', 'Evelyn Carter', 'Director of IT Operations', 'Primary delivery contact', 'evelyn.carter@greatlakes.example', '312-555-0142', '200 Lakeshore Drive', 'Chicago', 'IL', '60601', 'United States', true, 1),
        ('GLH', 'Marcus Hill', 'Contact Center Manager', 'Business owner', 'marcus.hill@greatlakes.example', '312-555-0188', '200 Lakeshore Drive', 'Chicago', 'IL', '60601', 'United States', false, 2),
        ('SMFG', 'Nadia Patel', 'VP of Operations', 'Project sponsor', 'nadia.patel@summitmfg.example', '616-555-0190', '4100 Industrial Parkway', 'Grand Rapids', 'MI', '49503', 'United States', true, 1),
        ('USS', 'Internal Platform Owner', 'Professional Services Operations', 'Internal product owner', 'psa-platform@ussignal.example', '800-555-0100', '201 Ionia Avenue SW', 'Grand Rapids', 'MI', '49503', 'United States', true, 1)
) AS seed(client_code, contact_name, title, role_description, email, phone, address_line1, city, state_region, postal_code, country, is_primary, display_order)
    ON seed.client_code = c.client_code
ON CONFLICT DO NOTHING;

CREATE OR REPLACE VIEW project_cost_status_vw AS
WITH assignment_hours AS (
    SELECT
        project_id,
        COALESCE(SUM(assigned_hours), 0) AS assigned_hours
    FROM project_assignments
    GROUP BY project_id
),
entry_hours AS (
    SELECT
        project_id,
        COALESCE(SUM(hours), 0) AS used_hours
    FROM time_entries
    WHERE project_id IS NOT NULL
    GROUP BY project_id
)
SELECT
    p.project_id,
    p.project_code,
    p.project_name,
    c.client_id,
    c.client_name,
    p.status AS project_status,
    p.billable,
    p.planned_engineering_cost,
    p.planned_pm_cost,
    p.planned_total_project_cost,
    COALESCE(ah.assigned_hours, 0) AS assigned_hours,
    COALESCE(eh.used_hours, 0) AS used_hours,
    GREATEST(COALESCE(ah.assigned_hours, 0) - COALESCE(eh.used_hours, 0), 0) AS remaining_assigned_hours,
    GREATEST(COALESCE(eh.used_hours, 0) - COALESCE(ah.assigned_hours, 0), 0) AS over_assigned_hours,
    CASE
        WHEN COALESCE(eh.used_hours, 0) > COALESCE(ah.assigned_hours, 0) AND COALESCE(ah.assigned_hours, 0) > 0 THEN 'hours_over_plan'
        WHEN p.planned_total_project_cost > 0 THEN 'cost_plan_loaded'
        ELSE 'cost_plan_missing'
    END AS cost_status
FROM projects p
LEFT JOIN clients c
    ON c.client_id = p.client_id
LEFT JOIN assignment_hours ah
    ON ah.project_id = p.project_id
LEFT JOIN entry_hours eh
    ON eh.project_id = p.project_id;

GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE client_contacts TO "ptp_app";
GRANT SELECT, UPDATE ON TABLE clients, projects, project_intake_requests TO "ptp_app";
GRANT SELECT ON TABLE project_cost_status_vw TO "ptp_app";
