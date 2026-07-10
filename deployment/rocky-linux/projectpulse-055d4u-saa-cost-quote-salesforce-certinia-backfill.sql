CREATE EXTENSION IF NOT EXISTS pgcrypto;

ALTER TABLE projects ADD COLUMN IF NOT EXISTS contract_type text NOT NULL DEFAULT '';
ALTER TABLE projects ADD COLUMN IF NOT EXISTS sow_signed_date date;
ALTER TABLE projects ADD COLUMN IF NOT EXISTS solution_architect_associate_user_id uuid REFERENCES app_users(user_id);
ALTER TABLE projects ADD COLUMN IF NOT EXISTS sell_quote_number text NOT NULL DEFAULT '';
ALTER TABLE projects ADD COLUMN IF NOT EXISTS salesforce_id_number text NOT NULL DEFAULT '';
ALTER TABLE projects ADD COLUMN IF NOT EXISTS certinia_id_number text NOT NULL DEFAULT '';
ALTER TABLE projects ADD COLUMN IF NOT EXISTS planned_total_project_cost numeric NOT NULL DEFAULT 0;
ALTER TABLE projects ADD COLUMN IF NOT EXISTS planned_pm_cost numeric NOT NULL DEFAULT 0;
ALTER TABLE projects ADD COLUMN IF NOT EXISTS planned_engineering_cost numeric NOT NULL DEFAULT 0;
ALTER TABLE projects ADD COLUMN IF NOT EXISTS planned_travel_cost numeric NOT NULL DEFAULT 0;
ALTER TABLE projects ADD COLUMN IF NOT EXISTS work_register_edit_metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb;

ALTER TABLE work_register_project_metadata ADD COLUMN IF NOT EXISTS sell_quote_number text NOT NULL DEFAULT '';
ALTER TABLE work_register_project_metadata ADD COLUMN IF NOT EXISTS salesforce_id_number text NOT NULL DEFAULT '';
ALTER TABLE work_register_project_metadata ADD COLUMN IF NOT EXISTS certinia_id_number text NOT NULL DEFAULT '';
ALTER TABLE work_register_project_metadata ADD COLUMN IF NOT EXISTS planned_total_project_cost numeric NOT NULL DEFAULT 0;
ALTER TABLE work_register_project_metadata ADD COLUMN IF NOT EXISTS planned_pm_cost numeric NOT NULL DEFAULT 0;
ALTER TABLE work_register_project_metadata ADD COLUMN IF NOT EXISTS planned_engineering_cost numeric NOT NULL DEFAULT 0;
ALTER TABLE work_register_project_metadata ADD COLUMN IF NOT EXISTS planned_travel_cost numeric NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS work_register_project_edit_save_audit (
    work_register_project_edit_save_audit_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id uuid,
    actor_user_id uuid,
    payload_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    result_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT NOW()
);

CREATE OR REPLACE FUNCTION pp055d4u_text(payload jsonb, VARIADIC keys text[])
RETURNS text
LANGUAGE plpgsql
AS $fn$
DECLARE
    k text;
    v text;
BEGIN
    IF payload IS NULL THEN
        RETURN NULL;
    END IF;

    FOREACH k IN ARRAY keys LOOP
        v := payload ->> k;
        IF v IS NOT NULL AND btrim(v) <> '' THEN
            RETURN v;
        END IF;
    END LOOP;

    RETURN NULL;
END;
$fn$;

CREATE OR REPLACE FUNCTION pp055d4u_uuid(value text)
RETURNS uuid
LANGUAGE plpgsql
AS $fn$
BEGIN
    IF value IS NULL OR btrim(value) = '' THEN
        RETURN NULL;
    END IF;

    IF value ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' THEN
        RETURN value::uuid;
    END IF;

    RETURN NULL;
END;
$fn$;

CREATE OR REPLACE FUNCTION pp055d4u_money(value text)
RETURNS numeric
LANGUAGE plpgsql
AS $fn$
DECLARE
    cleaned text;
BEGIN
    IF value IS NULL OR btrim(value) = '' THEN
        RETURN NULL;
    END IF;

    cleaned := regexp_replace(value, '[^0-9\.\-]+', '', 'g');

    IF cleaned IS NULL OR cleaned = '' OR cleaned = '-' OR cleaned = '.' THEN
        RETURN NULL;
    END IF;

    RETURN cleaned::numeric;
EXCEPTION WHEN others THEN
    RETURN NULL;
END;
$fn$;

CREATE OR REPLACE FUNCTION pp055d4u_date(value text)
RETURNS date
LANGUAGE plpgsql
AS $fn$
BEGIN
    IF value IS NULL OR btrim(value) = '' THEN
        RETURN NULL;
    END IF;

    RETURN value::date;
EXCEPTION WHEN others THEN
    RETURN NULL;
END;
$fn$;

CREATE OR REPLACE FUNCTION pp055d4u_contract(value text)
RETURNS text
LANGUAGE sql
AS $fn$
    SELECT CASE
        WHEN lower(coalesce(value, '')) IN ('fp', 'fixed price', 'fixedprice') THEN 'FP'
        WHEN lower(coalesce(value, '')) IN ('tm', 't&m', 'time and material', 'time & material', 'timeandmaterial') THEN 'T&M'
        ELSE coalesce(nullif(value, ''), '')
    END;
$fn$;

CREATE OR REPLACE FUNCTION pp055d4u_upsert_stakeholder(
    p_project_id uuid,
    p_role text,
    p_user_id uuid,
    p_actor_user_id uuid
)
RETURNS void
LANGUAGE plpgsql
AS $fn$
DECLARE
    v_display_name text := '';
    v_email text := '';
BEGIN
    IF p_project_id IS NULL OR p_user_id IS NULL THEN
        RETURN;
    END IF;

    SELECT coalesce(display_name, ''), coalesce(email, '')
    INTO v_display_name, v_email
    FROM app_users
    WHERE user_id = p_user_id;

    DELETE FROM work_register_project_stakeholders
    WHERE project_id = p_project_id
      AND lower(stakeholder_role) = lower(p_role);

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
    VALUES (
        p_project_id,
        p_role,
        p_user_id,
        v_display_name,
        v_email,
        'work_register_project_edit',
        p_actor_user_id,
        NOW()
    );
END;
$fn$;

CREATE OR REPLACE FUNCTION pp055d4u_apply_edit_payload(
    p_project_id uuid,
    p_actor_user_id uuid,
    p_payload jsonb
)
RETURNS jsonb
LANGUAGE plpgsql
AS $fn$
DECLARE
    v_project_id uuid;
    v_contract_type text;
    v_sow_signed_date date;
    v_saa_user_id uuid;
    v_total_cost numeric;
    v_pm_cost numeric;
    v_eng_cost numeric;
    v_travel_cost numeric;
    v_sell_quote_number text;
    v_salesforce_id_number text;
    v_certinia_id_number text;
    v_result jsonb;
BEGIN
    v_project_id := coalesce(
        p_project_id,
        pp055d4u_uuid(pp055d4u_text(p_payload, 'projectId', 'project_id', 'workId', 'work_id', 'id'))
    );

    IF v_project_id IS NULL THEN
        RETURN jsonb_build_object('status', 'skipped', 'message', 'No project_id found.');
    END IF;

    v_contract_type := pp055d4u_contract(pp055d4u_text(p_payload, 'contractType', 'contract_type', 'contract'));

    v_sow_signed_date := pp055d4u_date(pp055d4u_text(
        p_payload,
        'sowSignedDate',
        'sow_signed_date',
        'sowDate',
        'sow_date'
    ));

    v_saa_user_id := pp055d4u_uuid(pp055d4u_text(
        p_payload,
        'insideSalesUserId',
        'inside_sales_user_id',
        'saaUserId',
        'saa_user_id',
        'solutionArchitectAssociateUserId',
        'solution_architect_associate_user_id'
    ));

    v_total_cost := pp055d4u_money(pp055d4u_text(
        p_payload,
        'plannedTotalProjectCost',
        'planned_total_project_cost',
        'projectListPrice',
        'project_list_price',
        'totalCost',
        'total_cost',
        'budget',
        'contractValue',
        'sellAmount',
        'sellPrice'
    ));

    v_pm_cost := pp055d4u_money(pp055d4u_text(
        p_payload,
        'plannedPmCost',
        'planned_pm_cost',
        'pmCost',
        'projectManagementCost'
    ));

    v_eng_cost := pp055d4u_money(pp055d4u_text(
        p_payload,
        'plannedEngineeringCost',
        'planned_engineering_cost',
        'engineeringCost',
        'laborCost'
    ));

    v_travel_cost := pp055d4u_money(pp055d4u_text(
        p_payload,
        'plannedTravelCost',
        'planned_travel_cost',
        'travelCost'
    ));

    v_sell_quote_number := coalesce(pp055d4u_text(
        p_payload,
        'sellQuoteNumber',
        'sell_quote_number',
        'sellQuote',
        'sell_quote',
        'quoteNumber',
        'quote_number'
    ), '');

    v_salesforce_id_number := coalesce(pp055d4u_text(
        p_payload,
        'salesforceIdNumber',
        'salesforce_id_number',
        'salesforceId',
        'salesforce_id',
        'sfId',
        'sf_id',
        'opportunityId',
        'opportunity_id'
    ), '');

    v_certinia_id_number := coalesce(pp055d4u_text(
        p_payload,
        'certiniaIdNumber',
        'certinia_id_number',
        'certiniaId',
        'certinia_id',
        'certiniaProjectId',
        'certinia_project_id'
    ), '');

    UPDATE projects
    SET contract_type = CASE WHEN v_contract_type <> '' THEN v_contract_type ELSE contract_type END,
        sow_signed_date = coalesce(v_sow_signed_date, sow_signed_date),
        solution_architect_associate_user_id = coalesce(v_saa_user_id, solution_architect_associate_user_id),
        planned_total_project_cost = coalesce(v_total_cost, planned_total_project_cost),
        planned_pm_cost = coalesce(v_pm_cost, planned_pm_cost),
        planned_engineering_cost = coalesce(v_eng_cost, planned_engineering_cost),
        planned_travel_cost = coalesce(v_travel_cost, planned_travel_cost),
        sell_quote_number = CASE WHEN v_sell_quote_number <> '' THEN v_sell_quote_number ELSE sell_quote_number END,
        salesforce_id_number = CASE WHEN v_salesforce_id_number <> '' THEN v_salesforce_id_number ELSE salesforce_id_number END,
        certinia_id_number = CASE WHEN v_certinia_id_number <> '' THEN v_certinia_id_number ELSE certinia_id_number END,
        work_register_edit_metadata_json = coalesce(work_register_edit_metadata_json, '{}'::jsonb) || coalesce(p_payload, '{}'::jsonb),
        updated_at = NOW()
    WHERE project_id = v_project_id;

    IF v_saa_user_id IS NOT NULL THEN
        PERFORM pp055d4u_upsert_stakeholder(v_project_id, 'SAA', v_saa_user_id, p_actor_user_id);
        PERFORM pp055d4u_upsert_stakeholder(v_project_id, 'Inside Sales', v_saa_user_id, p_actor_user_id);
        PERFORM pp055d4u_upsert_stakeholder(v_project_id, 'Inside Sales / SAA', v_saa_user_id, p_actor_user_id);
        PERFORM pp055d4u_upsert_stakeholder(v_project_id, 'Solution Architect Associate', v_saa_user_id, p_actor_user_id);
        PERFORM pp055d4u_upsert_stakeholder(v_project_id, 'Solution Architect Associate / Inside Sales', v_saa_user_id, p_actor_user_id);
    END IF;

    INSERT INTO work_register_project_metadata (
        project_id,
        contract_type,
        sow_signed_date,
        project_list_price,
        planned_total_project_cost,
        planned_pm_cost,
        planned_engineering_cost,
        planned_travel_cost,
        sell_quote_number,
        salesforce_id_number,
        certinia_id_number,
        metadata_json,
        updated_at
    )
    VALUES (
        v_project_id,
        v_contract_type,
        v_sow_signed_date,
        coalesce(v_total_cost, 0),
        coalesce(v_total_cost, 0),
        coalesce(v_pm_cost, 0),
        coalesce(v_eng_cost, 0),
        coalesce(v_travel_cost, 0),
        v_sell_quote_number,
        v_salesforce_id_number,
        v_certinia_id_number,
        coalesce(p_payload, '{}'::jsonb),
        NOW()
    )
    ON CONFLICT (project_id) DO UPDATE
    SET contract_type = CASE WHEN EXCLUDED.contract_type <> '' THEN EXCLUDED.contract_type ELSE work_register_project_metadata.contract_type END,
        sow_signed_date = coalesce(EXCLUDED.sow_signed_date, work_register_project_metadata.sow_signed_date),
        project_list_price = CASE WHEN EXCLUDED.project_list_price <> 0 THEN EXCLUDED.project_list_price ELSE work_register_project_metadata.project_list_price END,
        planned_total_project_cost = CASE WHEN EXCLUDED.planned_total_project_cost <> 0 THEN EXCLUDED.planned_total_project_cost ELSE work_register_project_metadata.planned_total_project_cost END,
        planned_pm_cost = CASE WHEN EXCLUDED.planned_pm_cost <> 0 THEN EXCLUDED.planned_pm_cost ELSE work_register_project_metadata.planned_pm_cost END,
        planned_engineering_cost = CASE WHEN EXCLUDED.planned_engineering_cost <> 0 THEN EXCLUDED.planned_engineering_cost ELSE work_register_project_metadata.planned_engineering_cost END,
        planned_travel_cost = CASE WHEN EXCLUDED.planned_travel_cost <> 0 THEN EXCLUDED.planned_travel_cost ELSE work_register_project_metadata.planned_travel_cost END,
        sell_quote_number = CASE WHEN EXCLUDED.sell_quote_number <> '' THEN EXCLUDED.sell_quote_number ELSE work_register_project_metadata.sell_quote_number END,
        salesforce_id_number = CASE WHEN EXCLUDED.salesforce_id_number <> '' THEN EXCLUDED.salesforce_id_number ELSE work_register_project_metadata.salesforce_id_number END,
        certinia_id_number = CASE WHEN EXCLUDED.certinia_id_number <> '' THEN EXCLUDED.certinia_id_number ELSE work_register_project_metadata.certinia_id_number END,
        metadata_json = coalesce(work_register_project_metadata.metadata_json, '{}'::jsonb) || EXCLUDED.metadata_json,
        updated_at = NOW();

    v_result := jsonb_build_object(
        'status', 'applied',
        'projectId', v_project_id,
        'sowSignedDate', (SELECT sow_signed_date FROM projects WHERE project_id = v_project_id),
        'insideSalesUserId', (SELECT solution_architect_associate_user_id FROM projects WHERE project_id = v_project_id),
        'saaName', (
            SELECT display_name
            FROM app_users
            WHERE user_id = (SELECT solution_architect_associate_user_id FROM projects WHERE project_id = v_project_id)
        ),
        'plannedTotalProjectCost', (SELECT planned_total_project_cost FROM projects WHERE project_id = v_project_id),
        'sellQuoteNumber', (SELECT sell_quote_number FROM projects WHERE project_id = v_project_id),
        'salesforceIdNumber', (SELECT salesforce_id_number FROM projects WHERE project_id = v_project_id),
        'certiniaIdNumber', (SELECT certinia_id_number FROM projects WHERE project_id = v_project_id)
    );

    RETURN v_result;
END;
$fn$;

CREATE OR REPLACE FUNCTION pp055d4u_after_edit_audit()
RETURNS trigger
LANGUAGE plpgsql
AS $fn$
BEGIN
    PERFORM pp055d4u_apply_edit_payload(NEW.project_id, NEW.actor_user_id, NEW.payload_json);
    RETURN NEW;
END;
$fn$;

DROP TRIGGER IF EXISTS trg_pp055d4u_after_edit_audit ON work_register_project_edit_save_audit;

CREATE TRIGGER trg_pp055d4u_after_edit_audit
AFTER INSERT ON work_register_project_edit_save_audit
FOR EACH ROW
EXECUTE FUNCTION pp055d4u_after_edit_audit();

-- Backfill existing edit-save audit payloads.
SELECT pp055d4u_apply_edit_payload(project_id, actor_user_id, payload_json)
FROM work_register_project_edit_save_audit
WHERE project_id IS NOT NULL
ORDER BY created_at;
