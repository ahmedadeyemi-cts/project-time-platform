CREATE EXTENSION IF NOT EXISTS pgcrypto;

ALTER TABLE projects ADD COLUMN IF NOT EXISTS inside_sales_user_id uuid REFERENCES app_users(user_id);
ALTER TABLE projects ADD COLUMN IF NOT EXISTS saa_user_id uuid REFERENCES app_users(user_id);
ALTER TABLE projects ADD COLUMN IF NOT EXISTS inside_sales_name text NOT NULL DEFAULT '';
ALTER TABLE projects ADD COLUMN IF NOT EXISTS saa_name text NOT NULL DEFAULT '';
ALTER TABLE projects ADD COLUMN IF NOT EXISTS total_cost numeric NOT NULL DEFAULT 0;
ALTER TABLE projects ADD COLUMN IF NOT EXISTS project_list_price numeric NOT NULL DEFAULT 0;
ALTER TABLE projects ADD COLUMN IF NOT EXISTS sell_quote_number text NOT NULL DEFAULT '';
ALTER TABLE projects ADD COLUMN IF NOT EXISTS salesforce_id_number text NOT NULL DEFAULT '';
ALTER TABLE projects ADD COLUMN IF NOT EXISTS certinia_id_number text NOT NULL DEFAULT '';

-- Make sure the recursive AFTER trigger is gone.
DROP TRIGGER IF EXISTS trg_pp055d4v2_projects_alias_sync ON projects;
DROP FUNCTION IF EXISTS pp055d4v2_projects_alias_trigger();
DROP FUNCTION IF EXISTS pp055d4v2_sync_project_read_aliases(uuid);

-- Safe BEFORE trigger. This does not UPDATE projects, so it cannot recurse.
CREATE OR REPLACE FUNCTION pp055d4w_projects_alias_before()
RETURNS trigger
LANGUAGE plpgsql
AS $fn$
DECLARE
    v_saa_user_id uuid;
    v_saa_name text := '';
BEGIN
    v_saa_user_id := COALESCE(
        NEW.solution_architect_associate_user_id,
        NEW.inside_sales_user_id,
        NEW.saa_user_id
    );

    IF v_saa_user_id IS NOT NULL THEN
        SELECT COALESCE(display_name, '')
        INTO v_saa_name
        FROM app_users
        WHERE user_id = v_saa_user_id;

        NEW.solution_architect_associate_user_id := v_saa_user_id;
        NEW.inside_sales_user_id := v_saa_user_id;
        NEW.saa_user_id := v_saa_user_id;
        NEW.inside_sales_name := COALESCE(NULLIF(v_saa_name, ''), NEW.inside_sales_name, '');
        NEW.saa_name := COALESCE(NULLIF(v_saa_name, ''), NEW.saa_name, '');
    END IF;

    NEW.total_cost := CASE
        WHEN COALESCE(NEW.planned_total_project_cost, 0) <> 0 THEN NEW.planned_total_project_cost
        ELSE COALESCE(NEW.total_cost, 0)
    END;

    NEW.project_list_price := CASE
        WHEN COALESCE(NEW.planned_total_project_cost, 0) <> 0 THEN NEW.planned_total_project_cost
        ELSE COALESCE(NEW.project_list_price, 0)
    END;

    RETURN NEW;
END;
$fn$;

DROP TRIGGER IF EXISTS trg_pp055d4w_projects_alias_before ON projects;

CREATE TRIGGER trg_pp055d4w_projects_alias_before
BEFORE INSERT OR UPDATE OF
    solution_architect_associate_user_id,
    inside_sales_user_id,
    saa_user_id,
    planned_total_project_cost
ON projects
FOR EACH ROW
EXECUTE FUNCTION pp055d4w_projects_alias_before();

-- Safe one-time backfill.
WITH alias_values AS (
    SELECT p.project_id,
           COALESCE(p.solution_architect_associate_user_id, p.inside_sales_user_id, p.saa_user_id) AS resolved_saa_user_id,
           COALESCE(u.display_name, '') AS resolved_saa_name,
           CASE
               WHEN COALESCE(p.planned_total_project_cost, 0) <> 0 THEN p.planned_total_project_cost
               ELSE COALESCE(p.total_cost, 0)
           END AS resolved_total_cost,
           CASE
               WHEN COALESCE(p.planned_total_project_cost, 0) <> 0 THEN p.planned_total_project_cost
               ELSE COALESCE(p.project_list_price, 0)
           END AS resolved_project_list_price
    FROM projects p
    LEFT JOIN app_users u
      ON u.user_id = COALESCE(p.solution_architect_associate_user_id, p.inside_sales_user_id, p.saa_user_id)
)
UPDATE projects p
SET solution_architect_associate_user_id = COALESCE(alias_values.resolved_saa_user_id, p.solution_architect_associate_user_id),
    inside_sales_user_id = COALESCE(alias_values.resolved_saa_user_id, p.inside_sales_user_id),
    saa_user_id = COALESCE(alias_values.resolved_saa_user_id, p.saa_user_id),
    inside_sales_name = CASE
        WHEN alias_values.resolved_saa_name <> '' THEN alias_values.resolved_saa_name
        ELSE p.inside_sales_name
    END,
    saa_name = CASE
        WHEN alias_values.resolved_saa_name <> '' THEN alias_values.resolved_saa_name
        ELSE p.saa_name
    END,
    total_cost = alias_values.resolved_total_cost,
    project_list_price = alias_values.resolved_project_list_price,
    updated_at = NOW()
FROM alias_values
WHERE p.project_id = alias_values.project_id;

-- Ensure stakeholder alias rows exist for saved SAA values.
INSERT INTO work_register_project_stakeholders (
    project_id,
    stakeholder_role,
    user_id,
    display_name_snapshot,
    email_snapshot,
    source_system,
    created_by_user_id,
    created_at
)
SELECT p.project_id,
       alias_role.role_name,
       p.solution_architect_associate_user_id,
       COALESCE(u.display_name, ''),
       COALESCE(u.email, ''),
       'work_register_project_read_alias_bridge',
       NULL,
       NOW()
FROM projects p
JOIN app_users u
  ON u.user_id = p.solution_architect_associate_user_id
CROSS JOIN (
    VALUES
        ('SAA'),
        ('Inside Sales'),
        ('Inside Sales / SAA'),
        ('Solution Architect Associate'),
        ('Solution Architect Associate / Inside Sales')
) AS alias_role(role_name)
WHERE p.solution_architect_associate_user_id IS NOT NULL
  AND NOT EXISTS (
      SELECT 1
      FROM work_register_project_stakeholders existing
      WHERE existing.project_id = p.project_id
        AND lower(existing.stakeholder_role) = lower(alias_role.role_name)
  );
