CREATE EXTENSION IF NOT EXISTS pgcrypto;

ALTER TABLE projects ADD COLUMN IF NOT EXISTS contract_type text NOT NULL DEFAULT '';
ALTER TABLE projects ADD COLUMN IF NOT EXISTS sow_signed_date date;
ALTER TABLE projects ADD COLUMN IF NOT EXISTS project_coordinator_user_id uuid REFERENCES app_users(user_id);
ALTER TABLE projects ADD COLUMN IF NOT EXISTS solution_architect_associate_user_id uuid REFERENCES app_users(user_id);
ALTER TABLE projects ADD COLUMN IF NOT EXISTS sell_quote_number text NOT NULL DEFAULT '';
ALTER TABLE projects ADD COLUMN IF NOT EXISTS salesforce_id_number text NOT NULL DEFAULT '';
ALTER TABLE projects ADD COLUMN IF NOT EXISTS planned_total_project_cost numeric NOT NULL DEFAULT 0;
ALTER TABLE projects ADD COLUMN IF NOT EXISTS planned_pm_cost numeric NOT NULL DEFAULT 0;
ALTER TABLE projects ADD COLUMN IF NOT EXISTS planned_engineering_cost numeric NOT NULL DEFAULT 0;
ALTER TABLE projects ADD COLUMN IF NOT EXISTS work_register_edit_metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb;

ALTER TABLE work_register_project_metadata ADD COLUMN IF NOT EXISTS planned_total_project_cost numeric NOT NULL DEFAULT 0;
ALTER TABLE work_register_project_metadata ADD COLUMN IF NOT EXISTS planned_pm_cost numeric NOT NULL DEFAULT 0;
ALTER TABLE work_register_project_metadata ADD COLUMN IF NOT EXISTS planned_engineering_cost numeric NOT NULL DEFAULT 0;
ALTER TABLE work_register_project_metadata ADD COLUMN IF NOT EXISTS planned_travel_cost numeric NOT NULL DEFAULT 0;
ALTER TABLE work_register_project_metadata ADD COLUMN IF NOT EXISTS sell_quote_number text NOT NULL DEFAULT '';
ALTER TABLE work_register_project_metadata ADD COLUMN IF NOT EXISTS salesforce_id_number text NOT NULL DEFAULT '';

CREATE TABLE IF NOT EXISTS work_register_project_edit_save_audit (
    work_register_project_edit_save_audit_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id uuid,
    actor_user_id uuid,
    payload_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    result_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT NOW()
);

CREATE OR REPLACE FUNCTION pp055d4t_text(payload jsonb, key_name text)
RETURNS text
LANGUAGE plpgsql
AS $fn$
DECLARE
    value text;
BEGIN
    value := payload ->> key_name;

    IF value IS NULL OR btrim(value) = '' THEN
        RETURN NULL;
    END IF;

    RETURN value;
END;
$fn$;

CREATE OR REPLACE FUNCTION pp055d4t_uuid(value text)
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

CREATE OR REPLACE FUNCTION pp055d4t_money(value text)
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

CREATE OR REPLACE FUNCTION pp055d4t_contract(value text)
RETURNS text
LANGUAGE sql
AS $fn$
    SELECT CASE
        WHEN lower(coalesce(value, '')) IN ('fp', 'fixed price', 'fixedprice') THEN 'FP'
        WHEN lower(coalesce(value, '')) IN ('tm', 't&m', 'time and material', 'time & material', 'timeandmaterial') THEN 'T&M'
        ELSE coalesce(nullif(value, ''), '')
    END;
$fn$;

CREATE OR REPLACE FUNCTION pp055d4t_date(value text)
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

CREATE OR REPLACE FUNCTION pp055d4t_upsert_stakeholder(
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
    IF p_user_id IS NULL THEN
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

CREATE OR REPLACE FUNCTION projectpulse055d4m_update_project(
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
    v_pm_user_id uuid;
    v_pc_user_id uuid;
    v_ae_user_id uuid;
    v_sa_user_id uuid;
    v_total_cost numeric;
    v_pm_cost numeric;
    v_eng_cost numeric;
    v_quote text;
    v_salesforce text;
    v_result jsonb;
BEGIN
    v_project_id := pp055d4t_uuid(coalesce(
        pp055d4t_text(p_payload, 'projectId'),
        pp055d4t_text(p_payload, 'project_id'),
        pp055d4t_text(p_payload, 'workId'),
        pp055d4t_text(p_payload, 'work_id'),
        pp055d4t_text(p_payload, 'id')
    ));

    IF v_project_id IS NULL THEN
        v_result := jsonb_build_object(
            'status', 'validation_error',
            'message', 'Project ID is required for Work Register project update.',
            'payload', p_payload
        );

        INSERT INTO work_register_project_edit_save_audit (project_id, actor_user_id, payload_json, result_json)
        VALUES (NULL, p_actor_user_id, p_payload, v_result);

        RETURN v_result;
    END IF;

    v_contract_type := pp055d4t_contract(coalesce(
        pp055d4t_text(p_payload, 'contractType'),
        pp055d4t_text(p_payload, 'contract_type'),
        pp055d4t_text(p_payload, 'contract')
    ));

    v_sow_signed_date := pp055d4t_date(coalesce(
        pp055d4t_text(p_payload, 'sowSignedDate'),
        pp055d4t_text(p_payload, 'sow_signed_date'),
        pp055d4t_text(p_payload, 'sowDate'),
        pp055d4t_text(p_payload, 'sow_date')
    ));

    v_saa_user_id := pp055d4t_uuid(coalesce(
        pp055d4t_text(p_payload, 'insideSalesUserId'),
        pp055d4t_text(p_payload, 'inside_sales_user_id'),
        pp055d4t_text(p_payload, 'saaUserId'),
        pp055d4t_text(p_payload, 'saa_user_id'),
        pp055d4t_text(p_payload, 'solutionArchitectAssociateUserId'),
        pp055d4t_text(p_payload, 'solution_architect_associate_user_id')
    ));

    v_pm_user_id := pp055d4t_uuid(coalesce(
        pp055d4t_text(p_payload, 'projectManagerUserId'),
        pp055d4t_text(p_payload, 'pmUserId'),
        pp055d4t_text(p_payload, 'project_manager_user_id')
    ));

    v_pc_user_id := pp055d4t_uuid(coalesce(
        pp055d4t_text(p_payload, 'projectCoordinatorUserId'),
        pp055d4t_text(p_payload, 'projectTeamCoordinatorUserId'),
        pp055d4t_text(p_payload, 'pcUserId'),
        pp055d4t_text(p_payload, 'ptcUserId')
    ));

    v_ae_user_id := pp055d4t_uuid(coalesce(
        pp055d4t_text(p_payload, 'accountExecutiveUserId'),
        pp055d4t_text(p_payload, 'aeUserId'),
        pp055d4t_text(p_payload, 'account_executive_user_id')
    ));

    v_sa_user_id := pp055d4t_uuid(coalesce(
        pp055d4t_text(p_payload, 'solutionArchitectUserId'),
        pp055d4t_text(p_payload, 'saUserId'),
        pp055d4t_text(p_payload, 'solution_architect_user_id')
    ));

    v_total_cost := pp055d4t_money(coalesce(
        pp055d4t_text(p_payload, 'plannedTotalProjectCost'),
        pp055d4t_text(p_payload, 'planned_total_project_cost'),
        pp055d4t_text(p_payload, 'projectListPrice'),
        pp055d4t_text(p_payload, 'project_list_price'),
        pp055d4t_text(p_payload, 'totalCost'),
        pp055d4t_text(p_payload, 'budget'),
        pp055d4t_text(p_payload, 'contractValue'),
        pp055d4t_text(p_payload, 'sellAmount')
    ));

    v_pm_cost := pp055d4t_money(coalesce(
        pp055d4t_text(p_payload, 'plannedPmCost'),
        pp055d4t_text(p_payload, 'planned_pm_cost'),
        pp055d4t_text(p_payload, 'pmCost')
    ));

    v_eng_cost := pp055d4t_money(coalesce(
        pp055d4t_text(p_payload, 'plannedEngineeringCost'),
        pp055d4t_text(p_payload, 'planned_engineering_cost'),
        pp055d4t_text(p_payload, 'engineeringCost')
    ));

    v_quote := coalesce(
        pp055d4t_text(p_payload, 'sellQuoteNumber'),
        pp055d4t_text(p_payload, 'sell_quote_number'),
        pp055d4t_text(p_payload, 'sellQuote'),
        pp055d4t_text(p_payload, 'quoteNumber'),
        ''
    );

    v_salesforce := coalesce(
        pp055d4t_text(p_payload, 'salesforceIdNumber'),
        pp055d4t_text(p_payload, 'salesforce_id_number'),
        pp055d4t_text(p_payload, 'salesforceId'),
        pp055d4t_text(p_payload, 'sfId'),
        pp055d4t_text(p_payload, 'opportunityId'),
        ''
    );

    UPDATE projects
    SET contract_type = CASE WHEN v_contract_type <> '' THEN v_contract_type ELSE contract_type END,
        sow_signed_date = coalesce(v_sow_signed_date, sow_signed_date),
        solution_architect_associate_user_id = coalesce(v_saa_user_id, solution_architect_associate_user_id),
        project_manager_user_id = coalesce(v_pm_user_id, project_manager_user_id),
        project_coordinator_user_id = coalesce(v_pc_user_id, project_coordinator_user_id),
        account_executive_user_id = coalesce(v_ae_user_id, account_executive_user_id),
        solution_architect_user_id = coalesce(v_sa_user_id, solution_architect_user_id),
        planned_total_project_cost = coalesce(v_total_cost, planned_total_project_cost),
        planned_pm_cost = coalesce(v_pm_cost, planned_pm_cost),
        planned_engineering_cost = coalesce(v_eng_cost, planned_engineering_cost),
        sell_quote_number = CASE WHEN v_quote <> '' THEN v_quote ELSE sell_quote_number END,
        salesforce_id_number = CASE WHEN v_salesforce <> '' THEN v_salesforce ELSE salesforce_id_number END,
        work_register_edit_metadata_json = coalesce(work_register_edit_metadata_json, '{}'::jsonb) || p_payload,
        updated_at = NOW()
    WHERE project_id = v_project_id;

    IF v_saa_user_id IS NOT NULL THEN
        PERFORM pp055d4t_upsert_stakeholder(v_project_id, 'SAA', v_saa_user_id, p_actor_user_id);
        PERFORM pp055d4t_upsert_stakeholder(v_project_id, 'Inside Sales', v_saa_user_id, p_actor_user_id);
        PERFORM pp055d4t_upsert_stakeholder(v_project_id, 'Inside Sales / SAA', v_saa_user_id, p_actor_user_id);
        PERFORM pp055d4t_upsert_stakeholder(v_project_id, 'Solution Architect Associate', v_saa_user_id, p_actor_user_id);
    END IF;

    INSERT INTO work_register_project_metadata (
        project_id,
        contract_type,
        sow_signed_date,
        project_list_price,
        planned_total_project_cost,
        planned_pm_cost,
        planned_engineering_cost,
        sell_quote_number,
        salesforce_id_number,
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
        v_quote,
        v_salesforce,
        p_payload,
        NOW()
    )
    ON CONFLICT (project_id) DO UPDATE
    SET contract_type = CASE WHEN EXCLUDED.contract_type <> '' THEN EXCLUDED.contract_type ELSE work_register_project_metadata.contract_type END,
        sow_signed_date = coalesce(EXCLUDED.sow_signed_date, work_register_project_metadata.sow_signed_date),
        project_list_price = CASE WHEN EXCLUDED.project_list_price <> 0 THEN EXCLUDED.project_list_price ELSE work_register_project_metadata.project_list_price END,
        planned_total_project_cost = CASE WHEN EXCLUDED.planned_total_project_cost <> 0 THEN EXCLUDED.planned_total_project_cost ELSE work_register_project_metadata.planned_total_project_cost END,
        planned_pm_cost = CASE WHEN EXCLUDED.planned_pm_cost <> 0 THEN EXCLUDED.planned_pm_cost ELSE work_register_project_metadata.planned_pm_cost END,
        planned_engineering_cost = CASE WHEN EXCLUDED.planned_engineering_cost <> 0 THEN EXCLUDED.planned_engineering_cost ELSE work_register_project_metadata.planned_engineering_cost END,
        sell_quote_number = CASE WHEN EXCLUDED.sell_quote_number <> '' THEN EXCLUDED.sell_quote_number ELSE work_register_project_metadata.sell_quote_number END,
        salesforce_id_number = CASE WHEN EXCLUDED.salesforce_id_number <> '' THEN EXCLUDED.salesforce_id_number ELSE work_register_project_metadata.salesforce_id_number END,
        metadata_json = coalesce(work_register_project_metadata.metadata_json, '{}'::jsonb) || EXCLUDED.metadata_json,
        updated_at = NOW();

    v_result := jsonb_build_object(
        'status', 'updated',
        'message', 'Project edit saved.',
        'projectId', v_project_id,
        'contractType', (SELECT contract_type FROM projects WHERE project_id = v_project_id),
        'sowSignedDate', (SELECT sow_signed_date FROM projects WHERE project_id = v_project_id),
        'insideSalesUserId', (SELECT solution_architect_associate_user_id FROM projects WHERE project_id = v_project_id),
        'saaName', (
            SELECT display_name
            FROM app_users
            WHERE user_id = (SELECT solution_architect_associate_user_id FROM projects WHERE project_id = v_project_id)
        ),
        'plannedTotalProjectCost', (SELECT planned_total_project_cost FROM projects WHERE project_id = v_project_id),
        'plannedPmCost', (SELECT planned_pm_cost FROM projects WHERE project_id = v_project_id),
        'plannedEngineeringCost', (SELECT planned_engineering_cost FROM projects WHERE project_id = v_project_id),
        'sellQuoteNumber', (SELECT sell_quote_number FROM projects WHERE project_id = v_project_id),
        'salesforceIdNumber', (SELECT salesforce_id_number FROM projects WHERE project_id = v_project_id)
    );

    INSERT INTO work_register_project_edit_save_audit (project_id, actor_user_id, payload_json, result_json)
    VALUES (v_project_id, p_actor_user_id, p_payload, v_result);

    RETURN v_result;

EXCEPTION WHEN others THEN
    v_result := jsonb_build_object(
        'status', 'database_error',
        'message', SQLERRM,
        'sqlState', SQLSTATE,
        'projectId', v_project_id,
        'payload', p_payload
    );

    INSERT INTO work_register_project_edit_save_audit (project_id, actor_user_id, payload_json, result_json)
    VALUES (v_project_id, p_actor_user_id, p_payload, v_result);

    RETURN v_result;
END;
$fn$;
