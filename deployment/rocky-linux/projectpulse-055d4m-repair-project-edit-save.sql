CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE OR REPLACE FUNCTION projectpulse055d4m_json_text(payload jsonb, VARIADIC keys text[])
RETURNS text
LANGUAGE plpgsql
AS $$
DECLARE
    key text;
    value text;
BEGIN
    FOREACH key IN ARRAY keys LOOP
        IF payload ? key THEN
            value := payload->>key;
            IF value IS NOT NULL THEN
                RETURN value;
            END IF;
        END IF;
    END LOOP;

    RETURN NULL;
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4m_column_exists(table_name text, column_name text)
RETURNS boolean
LANGUAGE sql
AS $$
    SELECT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = $1
          AND column_name = $2
    );
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4m_numeric_or_null(value text)
RETURNS numeric
LANGUAGE plpgsql
AS $$
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
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4m_bool_or_null(value text)
RETURNS boolean
LANGUAGE plpgsql
AS $$
DECLARE
    normalized text;
BEGIN
    IF value IS NULL THEN
        RETURN NULL;
    END IF;

    normalized := lower(btrim(value));

    IF normalized IN ('true','t','yes','y','1') THEN RETURN TRUE; END IF;
    IF normalized IN ('false','f','no','n','0') THEN RETURN FALSE; END IF;

    RETURN NULL;
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4m_uuid_or_null(value text)
RETURNS uuid
LANGUAGE plpgsql
AS $$
BEGIN
    IF value IS NULL OR btrim(value) = '' THEN
        RETURN NULL;
    END IF;

    IF value ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' THEN
        RETURN value::uuid;
    END IF;

    RETURN NULL;
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4m_normalize_contract(value text)
RETURNS text
LANGUAGE sql
AS $$
    SELECT CASE
        WHEN lower(coalesce(value, '')) IN ('fp','fixed price','fixedprice') THEN 'FP'
        WHEN lower(coalesce(value, '')) IN ('tm','t&m','time and material','time & material','timeandmaterial') THEN 'T&M'
        ELSE coalesce(nullif(value, ''), 'Not set')
    END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4m_default_intake_reason(work_type text)
RETURNS text
LANGUAGE sql
AS $$
    SELECT CASE lower(coalesce(work_type, ''))
        WHEN 'project' THEN 'Creating a new Project.'
        WHEN 'iqs' THEN 'Creating a new IQS.'
        WHEN 'service request' THEN 'Creating a new Service Request.'
        WHEN 'internal project' THEN 'Creating a new Internal Project.'
        WHEN 'pre-sales' THEN 'Creating a new Pre-Sales work item.'
        WHEN 'presales' THEN 'Creating a new Pre-Sales work item.'
        ELSE 'Creating a new work item.'
    END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4m_set_project_column(
    p_project_id uuid,
    p_column_name text,
    p_value text
)
RETURNS integer
LANGUAGE plpgsql
AS $$
DECLARE
    v_count integer := 0;
BEGIN
    IF p_value IS NULL THEN
        RETURN 0;
    END IF;

    IF NOT projectpulse055d4m_column_exists('projects', p_column_name) THEN
        RETURN 0;
    END IF;

    EXECUTE format('UPDATE projects SET %I = $1, updated_at = NOW() WHERE project_id = $2', p_column_name)
    USING p_value, p_project_id;

    GET DIAGNOSTICS v_count = ROW_COUNT;
    RETURN v_count;
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4m_set_project_uuid_column(
    p_project_id uuid,
    p_column_name text,
    p_value uuid
)
RETURNS integer
LANGUAGE plpgsql
AS $$
DECLARE
    v_count integer := 0;
BEGIN
    IF p_value IS NULL THEN
        RETURN 0;
    END IF;

    IF NOT projectpulse055d4m_column_exists('projects', p_column_name) THEN
        RETURN 0;
    END IF;

    EXECUTE format('UPDATE projects SET %I = $1, updated_at = NOW() WHERE project_id = $2', p_column_name)
    USING p_value, p_project_id;

    GET DIAGNOSTICS v_count = ROW_COUNT;
    RETURN v_count;
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4m_set_project_numeric_column(
    p_project_id uuid,
    p_column_name text,
    p_value numeric
)
RETURNS integer
LANGUAGE plpgsql
AS $$
DECLARE
    v_count integer := 0;
BEGIN
    IF p_value IS NULL THEN
        RETURN 0;
    END IF;

    IF NOT projectpulse055d4m_column_exists('projects', p_column_name) THEN
        RETURN 0;
    END IF;

    EXECUTE format('UPDATE projects SET %I = $1, updated_at = NOW() WHERE project_id = $2', p_column_name)
    USING p_value, p_project_id;

    GET DIAGNOSTICS v_count = ROW_COUNT;
    RETURN v_count;
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4m_set_project_date_column(
    p_project_id uuid,
    p_column_name text,
    p_value text
)
RETURNS integer
LANGUAGE plpgsql
AS $$
DECLARE
    v_count integer := 0;
    v_date date;
BEGIN
    IF p_value IS NULL OR btrim(p_value) = '' THEN
        RETURN 0;
    END IF;

    IF NOT projectpulse055d4m_column_exists('projects', p_column_name) THEN
        RETURN 0;
    END IF;

    BEGIN
        v_date := p_value::date;
    EXCEPTION WHEN others THEN
        RETURN 0;
    END;

    EXECUTE format('UPDATE projects SET %I = $1, updated_at = NOW() WHERE project_id = $2', p_column_name)
    USING v_date, p_project_id;

    GET DIAGNOSTICS v_count = ROW_COUNT;
    RETURN v_count;
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4m_set_project_bool_column(
    p_project_id uuid,
    p_column_name text,
    p_value boolean
)
RETURNS integer
LANGUAGE plpgsql
AS $$
DECLARE
    v_count integer := 0;
BEGIN
    IF p_value IS NULL THEN
        RETURN 0;
    END IF;

    IF NOT projectpulse055d4m_column_exists('projects', p_column_name) THEN
        RETURN 0;
    END IF;

    EXECUTE format('UPDATE projects SET %I = $1, updated_at = NOW() WHERE project_id = $2', p_column_name)
    USING p_value, p_project_id;

    GET DIAGNOSTICS v_count = ROW_COUNT;
    RETURN v_count;
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4m_upsert_project_stakeholder(
    p_project_id uuid,
    p_role text,
    p_role_code text,
    p_role_name text,
    p_display_name text,
    p_user_id uuid,
    p_actor_user_id uuid
)
RETURNS uuid
LANGUAGE plpgsql
AS $$
DECLARE
    v_user_id uuid;
    v_display_name text;
    v_email text;
BEGIN
    v_user_id := p_user_id;
    v_display_name := btrim(coalesce(p_display_name, ''));

    IF v_user_id IS NULL AND v_display_name <> '' THEN
        -- This function prefers an existing @ussignal.com user first.
        -- If not found, it creates a guarded non-login @ussignal.cloud placeholder.
        v_user_id := projectpulse055d4d_get_or_create_stakeholder_user(
            v_display_name,
            p_role_code,
            p_role_name,
            p_role_name,
            p_role_name,
            p_actor_user_id
        );
    END IF;

    IF v_user_id IS NOT NULL THEN
        SELECT display_name, email
        INTO v_display_name, v_email
        FROM app_users
        WHERE user_id = v_user_id;

        v_display_name := coalesce(nullif(v_display_name, ''), p_display_name, '');
        v_email := coalesce(v_email, '');
    ELSE
        v_email := '';
    END IF;

    IF v_user_id IS NULL AND v_display_name = '' THEN
        RETURN NULL;
    END IF;

    UPDATE work_register_project_stakeholders
    SET user_id = v_user_id,
        display_name_snapshot = v_display_name,
        email_snapshot = v_email,
        source_system = 'work_register_project_edit',
        created_by_user_id = p_actor_user_id,
        created_at = NOW()
    WHERE project_id = p_project_id
      AND lower(stakeholder_role) = lower(p_role);

    IF NOT FOUND THEN
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
            v_user_id,
            v_display_name,
            v_email,
            'work_register_project_edit',
            p_actor_user_id,
            NOW()
        );
    END IF;

    RETURN v_user_id;
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4m_update_project(
    p_actor_user_id uuid,
    p_payload jsonb
)
RETURNS jsonb
LANGUAGE plpgsql
AS $$
DECLARE
    v_project_id uuid;
    v_project_exists boolean;
    v_update_count integer := 0;
    v_stakeholder_count integer := 0;
    v_metadata_count integer := 0;
    v_user_id uuid;
    v_contract_type text;
    v_sow_signed_date date;
    v_requested_work_type text;
    v_intake_reason text;
    v_project_list_price numeric;
    v_pm_hours numeric;
    v_engineering_hours numeric;
    v_travel_hours numeric;
BEGIN
    v_project_id := projectpulse055d4m_uuid_or_null(projectpulse055d4m_json_text(p_payload, 'projectId', 'project_id', 'id'));

    IF v_project_id IS NULL THEN
        RETURN jsonb_build_object(
            'status', 'validation_error',
            'message', 'Project ID is required for Work Register project update.'
        );
    END IF;

    SELECT EXISTS (SELECT 1 FROM projects WHERE project_id = v_project_id)
    INTO v_project_exists;

    IF NOT v_project_exists THEN
        RETURN jsonb_build_object(
            'status', 'not_found',
            'message', 'Project was not found.'
        );
    END IF;

    v_update_count := v_update_count + projectpulse055d4m_set_project_column(v_project_id, 'project_name', projectpulse055d4m_json_text(p_payload, 'projectName', 'project_name', 'name'));
    v_update_count := v_update_count + projectpulse055d4m_set_project_column(v_project_id, 'project_description', projectpulse055d4m_json_text(p_payload, 'projectDescription', 'project_description', 'description'));
    v_update_count := v_update_count + projectpulse055d4m_set_project_column(v_project_id, 'status', projectpulse055d4m_json_text(p_payload, 'status', 'projectStatus'));
    v_update_count := v_update_count + projectpulse055d4m_set_project_date_column(v_project_id, 'start_date', projectpulse055d4m_json_text(p_payload, 'startDate', 'start_date'));
    v_update_count := v_update_count + projectpulse055d4m_set_project_date_column(v_project_id, 'end_date', projectpulse055d4m_json_text(p_payload, 'endDate', 'end_date'));
    v_update_count := v_update_count + projectpulse055d4m_set_project_bool_column(v_project_id, 'billable', projectpulse055d4m_bool_or_null(projectpulse055d4m_json_text(p_payload, 'billable', 'isBillable')));

    v_update_count := v_update_count + projectpulse055d4m_set_project_numeric_column(v_project_id, 'planned_total_project_cost', projectpulse055d4m_numeric_or_null(projectpulse055d4m_json_text(p_payload, 'plannedTotalProjectCost', 'projectListPrice', 'project_list_price', 'totalCost', 'total_cost')));
    v_update_count := v_update_count + projectpulse055d4m_set_project_numeric_column(v_project_id, 'planned_pm_cost', projectpulse055d4m_numeric_or_null(projectpulse055d4m_json_text(p_payload, 'plannedPmCost', 'planned_pm_cost', 'pmCost')));
    v_update_count := v_update_count + projectpulse055d4m_set_project_numeric_column(v_project_id, 'planned_engineering_cost', projectpulse055d4m_numeric_or_null(projectpulse055d4m_json_text(p_payload, 'plannedEngineeringCost', 'planned_engineering_cost', 'engineeringCost')));

    v_user_id := projectpulse055d4m_uuid_or_null(projectpulse055d4m_json_text(p_payload, 'projectManagerUserId', 'project_manager_user_id', 'pmUserId'));
    v_update_count := v_update_count + projectpulse055d4m_set_project_uuid_column(v_project_id, 'project_manager_user_id', v_user_id);

    v_user_id := projectpulse055d4m_uuid_or_null(projectpulse055d4m_json_text(p_payload, 'accountExecutiveUserId', 'account_executive_user_id', 'aeUserId'));
    v_update_count := v_update_count + projectpulse055d4m_set_project_uuid_column(v_project_id, 'account_executive_user_id', v_user_id);

    IF projectpulse055d4m_json_text(p_payload, 'accountExecutiveName', 'accountExecutive', 'aeName') IS NOT NULL OR v_user_id IS NOT NULL THEN
        PERFORM projectpulse055d4m_upsert_project_stakeholder(
            v_project_id,
            'Account Executive',
            'ACCOUNT_EXECUTIVE',
            'Sales',
            projectpulse055d4m_json_text(p_payload, 'accountExecutiveName', 'accountExecutive', 'aeName'),
            v_user_id,
            p_actor_user_id
        );
        v_stakeholder_count := v_stakeholder_count + 1;
    END IF;

    v_user_id := projectpulse055d4m_uuid_or_null(projectpulse055d4m_json_text(p_payload, 'solutionArchitectUserId', 'solution_architect_user_id', 'saUserId'));
    v_update_count := v_update_count + projectpulse055d4m_set_project_uuid_column(v_project_id, 'solution_architect_user_id', v_user_id);

    IF projectpulse055d4m_json_text(p_payload, 'solutionArchitectName', 'solutionArchitect', 'saName') IS NOT NULL OR v_user_id IS NOT NULL THEN
        PERFORM projectpulse055d4m_upsert_project_stakeholder(
            v_project_id,
            'Solution Architect',
            'SOLUTION_ARCHITECT',
            'Solution Architect',
            projectpulse055d4m_json_text(p_payload, 'solutionArchitectName', 'solutionArchitect', 'saName'),
            v_user_id,
            p_actor_user_id
        );
        v_stakeholder_count := v_stakeholder_count + 1;
    END IF;

    v_user_id := projectpulse055d4m_uuid_or_null(projectpulse055d4m_json_text(p_payload, 'solutionArchitectAssociateUserId', 'saaUserId', 'insideSalesUserId'));
    IF projectpulse055d4m_json_text(p_payload, 'solutionArchitectAssociateName', 'saaName', 'insideSalesName', 'insideSales') IS NOT NULL OR v_user_id IS NOT NULL THEN
        PERFORM projectpulse055d4m_upsert_project_stakeholder(
            v_project_id,
            'Solution Architect Associate',
            'SOLUTION_ARCHITECT_ASSOCIATE',
            'Inside Sales',
            projectpulse055d4m_json_text(p_payload, 'solutionArchitectAssociateName', 'saaName', 'insideSalesName', 'insideSales'),
            v_user_id,
            p_actor_user_id
        );
        v_stakeholder_count := v_stakeholder_count + 1;
    END IF;

    v_user_id := projectpulse055d4m_uuid_or_null(projectpulse055d4m_json_text(p_payload, 'projectCoordinatorUserId', 'projectTeamCoordinatorUserId', 'ptcUserId', 'pcUserId'));
    IF projectpulse055d4m_json_text(p_payload, 'projectCoordinatorName', 'projectTeamCoordinatorName', 'ptcName', 'pcName') IS NOT NULL OR v_user_id IS NOT NULL THEN
        PERFORM projectpulse055d4m_upsert_project_stakeholder(
            v_project_id,
            'Project Team Coordinator',
            'PROJECT_TEAM_COORDINATOR',
            'Project Team Coordinator',
            projectpulse055d4m_json_text(p_payload, 'projectCoordinatorName', 'projectTeamCoordinatorName', 'ptcName', 'pcName'),
            v_user_id,
            p_actor_user_id
        );
        v_stakeholder_count := v_stakeholder_count + 1;
    END IF;

    v_contract_type := projectpulse055d4m_json_text(p_payload, 'contractType', 'contract_type', 'contract');
    v_requested_work_type := coalesce(projectpulse055d4m_json_text(p_payload, 'requestedWorkType', 'workType', 'work_type'), 'Project');

    BEGIN
        v_sow_signed_date := NULLIF(projectpulse055d4m_json_text(p_payload, 'sowSignedDate', 'sow_signed_date'), '')::date;
    EXCEPTION WHEN others THEN
        v_sow_signed_date := NULL;
    END;

    v_project_list_price := projectpulse055d4m_numeric_or_null(projectpulse055d4m_json_text(p_payload, 'projectListPrice', 'project_list_price', 'plannedTotalProjectCost', 'planned_total_project_cost', 'totalCost'));
    v_pm_hours := projectpulse055d4m_numeric_or_null(projectpulse055d4m_json_text(p_payload, 'pmHours', 'pm_hours'));
    v_engineering_hours := projectpulse055d4m_numeric_or_null(projectpulse055d4m_json_text(p_payload, 'engineeringHours', 'engineering_hours'));
    v_travel_hours := projectpulse055d4m_numeric_or_null(projectpulse055d4m_json_text(p_payload, 'travelHours', 'travel_hours'));
    v_intake_reason := coalesce(projectpulse055d4m_json_text(p_payload, 'intakeReason', 'intake_reason'), projectpulse055d4m_default_intake_reason(v_requested_work_type));

    IF v_contract_type IS NOT NULL
       OR v_sow_signed_date IS NOT NULL
       OR v_project_list_price IS NOT NULL
       OR v_pm_hours IS NOT NULL
       OR v_engineering_hours IS NOT NULL
       OR v_travel_hours IS NOT NULL
       OR v_intake_reason IS NOT NULL
    THEN
        INSERT INTO work_register_project_metadata (
            project_id,
            requested_work_type,
            contract_type,
            sow_signed_date,
            intake_reason,
            project_list_price,
            pm_hours,
            engineering_hours,
            travel_hours,
            metadata_json,
            updated_at
        )
        VALUES (
            v_project_id,
            v_requested_work_type,
            projectpulse055d4m_normalize_contract(v_contract_type),
            v_sow_signed_date,
            v_intake_reason,
            coalesce(v_project_list_price, 0),
            coalesce(v_pm_hours, 0),
            coalesce(v_engineering_hours, 0),
            coalesce(v_travel_hours, 0),
            p_payload,
            NOW()
        )
        ON CONFLICT (project_id) DO UPDATE
        SET requested_work_type = coalesce(nullif(EXCLUDED.requested_work_type, ''), work_register_project_metadata.requested_work_type),
            contract_type = coalesce(nullif(EXCLUDED.contract_type, ''), work_register_project_metadata.contract_type),
            sow_signed_date = coalesce(EXCLUDED.sow_signed_date, work_register_project_metadata.sow_signed_date),
            intake_reason = coalesce(nullif(EXCLUDED.intake_reason, ''), work_register_project_metadata.intake_reason),
            project_list_price = CASE WHEN EXCLUDED.project_list_price > 0 THEN EXCLUDED.project_list_price ELSE work_register_project_metadata.project_list_price END,
            pm_hours = CASE WHEN EXCLUDED.pm_hours > 0 THEN EXCLUDED.pm_hours ELSE work_register_project_metadata.pm_hours END,
            engineering_hours = CASE WHEN EXCLUDED.engineering_hours > 0 THEN EXCLUDED.engineering_hours ELSE work_register_project_metadata.engineering_hours END,
            travel_hours = CASE WHEN EXCLUDED.travel_hours > 0 THEN EXCLUDED.travel_hours ELSE work_register_project_metadata.travel_hours END,
            metadata_json = work_register_project_metadata.metadata_json || EXCLUDED.metadata_json,
            updated_at = NOW();

        v_metadata_count := 1;
    END IF;

    INSERT INTO work_register_change_history (
        work_register_change_history_id,
        source_table,
        work_id,
        action,
        change_summary,
        changed_fields_csv,
        changed_by_user_id,
        old_value_json,
        new_value_json,
        changed_at
    )
    VALUES (
        gen_random_uuid(),
        'projects',
        v_project_id,
        'work_register_project_edit_saved',
        'Work Register project edit saved. Project fields: ' || v_update_count || '. Stakeholders: ' || v_stakeholder_count || '. Metadata: ' || v_metadata_count || '.',
        'project,stakeholders,metadata',
        p_actor_user_id,
        NULL,
        p_payload,
        NOW()
    );

    RETURN jsonb_build_object(
        'status', 'updated',
        'message', 'Project edit saved.',
        'projectId', v_project_id,
        'projectFieldsUpdated', v_update_count,
        'stakeholdersUpdated', v_stakeholder_count,
        'metadataUpdated', v_metadata_count
    );

EXCEPTION WHEN others THEN
    RETURN jsonb_build_object(
        'status', 'database_error',
        'message', SQLERRM,
        'sqlState', SQLSTATE,
        'projectId', v_project_id
    );
END;
$$;
