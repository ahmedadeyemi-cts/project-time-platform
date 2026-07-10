CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS work_register_intake_commits (
    work_register_intake_commit_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    work_register_intake_package_id uuid NOT NULL UNIQUE REFERENCES work_register_intake_packages(work_register_intake_package_id),
    project_id uuid NOT NULL REFERENCES projects(project_id),
    project_code text NOT NULL,
    committed_by_user_id uuid REFERENCES app_users(user_id),
    committed_at timestamptz NOT NULL DEFAULT NOW(),
    commit_summary_json jsonb NOT NULL DEFAULT '{}'::jsonb
);

CREATE TABLE IF NOT EXISTS work_register_project_stakeholders (
    work_register_project_stakeholder_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id uuid NOT NULL REFERENCES projects(project_id),
    stakeholder_role text NOT NULL,
    user_id uuid REFERENCES app_users(user_id),
    display_name_snapshot text NOT NULL DEFAULT '',
    email_snapshot text NOT NULL DEFAULT '',
    source_system text NOT NULL DEFAULT 'work_register_intake',
    created_by_user_id uuid REFERENCES app_users(user_id),
    created_at timestamptz NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS work_register_project_documents (
    work_register_project_document_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id uuid NOT NULL REFERENCES projects(project_id),
    work_register_intake_package_id uuid REFERENCES work_register_intake_packages(work_register_intake_package_id),
    work_register_intake_document_id uuid REFERENCES work_register_intake_documents(work_register_intake_document_id),
    document_type text NOT NULL,
    original_file_name text NOT NULL,
    stored_file_path text NOT NULL,
    content_type text NOT NULL,
    file_size_bytes bigint NOT NULL DEFAULT 0,
    created_by_user_id uuid REFERENCES app_users(user_id),
    created_at timestamptz NOT NULL DEFAULT NOW()
);

ALTER TABLE work_register_project_documents
    ADD COLUMN IF NOT EXISTS customer_folder_path text,
    ADD COLUMN IF NOT EXISTS copied_to_customer_folder_at timestamptz,
    ADD COLUMN IF NOT EXISTS document_routing_status text NOT NULL DEFAULT 'linked';

CREATE TABLE IF NOT EXISTS work_register_project_metadata (
    work_register_project_metadata_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id uuid NOT NULL UNIQUE REFERENCES projects(project_id),
    work_register_intake_package_id uuid REFERENCES work_register_intake_packages(work_register_intake_package_id),
    requested_work_type text NOT NULL DEFAULT '',
    contract_type text NOT NULL DEFAULT '',
    gsd_template_family text NOT NULL DEFAULT 'standard',
    sow_signed_date date,
    intake_reason text NOT NULL DEFAULT '',
    project_list_price numeric NOT NULL DEFAULT 0,
    pm_hours numeric NOT NULL DEFAULT 0,
    engineering_hours numeric NOT NULL DEFAULT 0,
    travel_hours numeric NOT NULL DEFAULT 0,
    metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_by_user_id uuid REFERENCES app_users(user_id),
    created_at timestamptz NOT NULL DEFAULT NOW(),
    updated_at timestamptz NOT NULL DEFAULT NOW()
);

CREATE OR REPLACE FUNCTION projectpulse055d4d_numeric_or_zero(value text)
RETURNS numeric
LANGUAGE plpgsql
AS $$
DECLARE
    cleaned text;
BEGIN
    IF value IS NULL OR btrim(value) = '' THEN RETURN 0; END IF;
    cleaned := regexp_replace(value, '[^0-9\.\-]+', '', 'g');
    IF cleaned IS NULL OR cleaned = '' OR cleaned = '-' OR cleaned = '.' THEN RETURN 0; END IF;
    RETURN cleaned::numeric;
EXCEPTION WHEN others THEN
    RETURN 0;
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4d_bool_or_default(value text, default_value boolean)
RETURNS boolean
LANGUAGE plpgsql
AS $$
DECLARE
    normalized text;
BEGIN
    normalized := lower(btrim(coalesce(value, '')));
    IF normalized = '' THEN RETURN default_value; END IF;
    IF normalized IN ('true','t','yes','y','1') THEN RETURN TRUE; END IF;
    IF normalized IN ('false','f','no','n','0') THEN RETURN FALSE; END IF;
    RETURN default_value;
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4d_uuid_or_null(value text)
RETURNS uuid
LANGUAGE plpgsql
AS $$
BEGIN
    IF value IS NULL OR btrim(value) = '' THEN RETURN NULL; END IF;
    IF value ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' THEN
        RETURN value::uuid;
    END IF;
    RETURN NULL;
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4d_email_from_name(display_name text)
RETURNS text
LANGUAGE plpgsql
AS $$
DECLARE
    cleaned text;
    parts text[];
BEGIN
    cleaned := lower(regexp_replace(coalesce(display_name, ''), '[^a-zA-Z0-9 ]+', ' ', 'g'));
    cleaned := regexp_replace(cleaned, '\s+', ' ', 'g');
    cleaned := btrim(cleaned);

    IF cleaned = '' THEN RETURN ''; END IF;

    parts := regexp_split_to_array(cleaned, '\s+');
    RETURN parts[1] || '.' || parts[array_length(parts, 1)] || '@ussignal.com';
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4d_existing_source_provider()
RETURNS text
LANGUAGE plpgsql
AS $$
DECLARE
    provider text;
BEGIN
    SELECT source_provider
    INTO provider
    FROM app_users
    WHERE source_provider IS NOT NULL AND btrim(source_provider) <> ''
    GROUP BY source_provider
    ORDER BY count(*) DESC
    LIMIT 1;

    RETURN coalesce(NULLIF(provider, ''), 'manual');
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4d_get_or_create_stakeholder_user(
    p_display_name text,
    p_role_code text,
    p_role_name text,
    p_job_title text,
    p_team_name text,
    p_actor_user_id uuid
)
RETURNS uuid
LANGUAGE plpgsql
AS $$
DECLARE
    v_display_name text;
    v_email text;
    v_user_id uuid;
    v_app_role_id uuid;
BEGIN
    v_display_name := btrim(coalesce(p_display_name, ''));
    IF v_display_name = '' THEN RETURN NULL; END IF;

    v_email := projectpulse055d4d_email_from_name(v_display_name);
    IF v_email = '' THEN RETURN NULL; END IF;

    SELECT user_id
    INTO v_user_id
    FROM app_users
    WHERE lower(email) = lower(v_email)
       OR lower(display_name) = lower(v_display_name)
    ORDER BY is_active DESC, updated_at DESC
    LIMIT 1;

    IF v_user_id IS NULL THEN
        v_user_id := gen_random_uuid();

        INSERT INTO app_users (
            user_id, entra_object_id, email, display_name, employee_number,
            job_title, department, is_active, created_at, updated_at,
            source_provider, department_name, office_location, manager_email,
            login_enabled, team_name
        )
        VALUES (
            v_user_id, 'work-register-intake-temp:' || v_user_id::text, v_email, v_display_name, NULL,
            p_job_title, p_team_name, TRUE, NOW(), NOW(),
            projectpulse055d4d_existing_source_provider(), p_team_name, NULL, NULL,
            FALSE, p_team_name
        );
    ELSE
        UPDATE app_users
        SET email = COALESCE(NULLIF(email, ''), v_email),
            display_name = COALESCE(NULLIF(display_name, ''), v_display_name),
            job_title = COALESCE(NULLIF(job_title, ''), p_job_title),
            department = COALESCE(NULLIF(department, ''), p_team_name),
            department_name = COALESCE(NULLIF(department_name, ''), p_team_name),
            team_name = COALESCE(NULLIF(team_name, ''), p_team_name),
            is_active = TRUE,
            updated_at = NOW()
        WHERE user_id = v_user_id;
    END IF;

    SELECT app_role_id INTO v_app_role_id
    FROM app_roles
    WHERE role_code = p_role_code
    LIMIT 1;

    IF v_app_role_id IS NULL THEN
        v_app_role_id := gen_random_uuid();

        INSERT INTO app_roles (
            app_role_id, role_code, role_name, role_description,
            is_system_role, is_active, display_order, created_at, updated_at
        )
        VALUES (
            v_app_role_id, p_role_code, p_role_name,
            'Created or linked by Work Register intake final save for stakeholder notification routing.',
            FALSE, TRUE, 900, NOW(), NOW()
        );
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM app_user_role_assignments
        WHERE user_id = v_user_id
          AND app_role_id = v_app_role_id
          AND is_active = TRUE
    ) THEN
        INSERT INTO app_user_role_assignments (
            app_user_role_assignment_id, user_id, app_role_id,
            assigned_by_user_id, assignment_reason, is_active, assigned_at, updated_at
        )
        VALUES (
            gen_random_uuid(), v_user_id, v_app_role_id,
            p_actor_user_id, 'Linked by Work Register intake final save.', TRUE, NOW(), NOW()
        );
    END IF;

    RETURN v_user_id;
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4d_commit_intake_package(
    p_intake_package_id uuid,
    p_actor_user_id uuid
)
RETURNS jsonb
LANGUAGE plpgsql
AS $$
DECLARE
    v_pkg record;
    v_existing record;
    v_review jsonb;
    v_project_id uuid;
    v_project_code text;
    v_project_name text;
    v_customer_id uuid;
    v_requested_work_type text;
    v_contract_type text;
    v_template_family text;
    v_intake_reason text;
    v_sow_signed_date date;
    v_pm_user_id uuid;
    v_pc_user_id uuid;
    v_ae_user_id uuid;
    v_sa_user_id uuid;
    v_saa_user_id uuid;
    v_total_cost numeric := 0;
    v_pm_cost numeric := 0;
    v_engineering_cost numeric := 0;
    v_project_status text := 'active';
    v_task_utilization_bucket text := 'billable';
    v_task_work_category text := 'project_task';
    v_task_billing_classification text := 'billable';
    v_task_utilization_classification text := 'billable_utilization';
    v_rate_card_id uuid;
    v_rate_card_type text := 'standard';
    v_rate_card_status text := 'active';
    v_rate_card_source_system text := 'Work Register GSD Intake';
    v_rate record;
    v_task record;
    v_assignment record;
    v_task_id uuid;
    v_task_idx int := 0;
    v_rate_idx int := 0;
    v_task_count int := 0;
    v_assignment_count int := 0;
    v_rate_count int := 0;
    v_document_count int := 0;
    v_stakeholder_count int := 0;
    v_warnings jsonb := '[]'::jsonb;
    v_commit_json jsonb;
    v_message text;
    v_detail text;
    v_hint text;
    v_context text;
    v_sqlstate text;
BEGIN
    SELECT *
    INTO v_existing
    FROM work_register_intake_commits
    WHERE work_register_intake_package_id = p_intake_package_id
    LIMIT 1;

    IF FOUND THEN
        RETURN jsonb_build_object(
            'status', 'already_committed',
            'message', 'This intake package was already committed.',
            'projectId', v_existing.project_id,
            'projectCode', v_existing.project_code
        );
    END IF;

    SELECT *
    INTO v_pkg
    FROM work_register_intake_packages
    WHERE work_register_intake_package_id = p_intake_package_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RETURN jsonb_build_object('status', 'not_found', 'message', 'Intake package was not found.');
    END IF;

    v_review := coalesce(v_pkg.reviewed_json, '{}'::jsonb);

    IF v_review = '{}'::jsonb THEN
        RETURN jsonb_build_object('status', 'validation_error', 'message', 'Save Assignment Configuration before creating Work Register.');
    END IF;

    v_customer_id := coalesce(projectpulse055d4d_uuid_or_null(v_review->>'customerId'), v_pkg.customer_id);

    IF v_customer_id IS NULL THEN
        RETURN jsonb_build_object('status', 'validation_error', 'message', 'Customer is required before final save.');
    END IF;

    v_project_name := btrim(coalesce(NULLIF(v_review->>'projectName', ''), NULLIF(v_pkg.project_name_hint, ''), 'Untitled Work Register Intake'));
    v_requested_work_type := btrim(coalesce(NULLIF(v_review->>'requestedWorkType', ''), NULLIF(v_pkg.requested_work_type, ''), 'Project'));
    v_contract_type := btrim(coalesce(NULLIF(v_review->>'contractType', ''), NULLIF(v_pkg.contract_type, ''), 'FP'));
    v_template_family := btrim(coalesce(NULLIF(v_review->>'gsdTemplateFamily', ''), NULLIF(v_review->>'templateFamily', ''), 'standard'));
    v_intake_reason := btrim(coalesce(NULLIF(v_review->>'intakeReason', ''), NULLIF(v_pkg.notes, ''), v_requested_work_type || ' intake for ' || v_project_name || ' created from ProjectPulse intake wizard.'));

    BEGIN
        v_sow_signed_date := NULLIF(v_review->>'sowSignedDate', '')::date;
    EXCEPTION WHEN others THEN
        v_sow_signed_date := NULL;
        v_warnings := v_warnings || jsonb_build_array(jsonb_build_object('scope', 'sowSignedDate', 'message', 'Invalid SOW signed date ignored.'));
    END;

    IF lower(v_contract_type) IN ('t&m','tm','time and material','time & material','timeandmaterial') THEN
        v_contract_type := 'TM';
    ELSIF lower(v_contract_type) IN ('fixed price','fixedprice','fp') THEN
        v_contract_type := 'FP';
    END IF;

    v_pm_user_id := projectpulse055d4d_uuid_or_null(v_review #>> '{assignmentPlan,projectManagerUserId}');
    v_pc_user_id := projectpulse055d4d_uuid_or_null(v_review #>> '{assignmentPlan,projectCoordinatorUserId}');

    v_ae_user_id := projectpulse055d4d_get_or_create_stakeholder_user(v_review->>'accountExecutiveName', 'ACCOUNT_EXECUTIVE', 'Account Executive', 'Account Executive', 'Sales', p_actor_user_id);
    v_sa_user_id := projectpulse055d4d_get_or_create_stakeholder_user(v_review->>'solutionArchitectName', 'SOLUTION_ARCHITECT', 'Solution Architect', 'Solution Architect', 'Solution Architecture', p_actor_user_id);
    v_saa_user_id := projectpulse055d4d_get_or_create_stakeholder_user(v_review->>'insideSalesName', 'SOLUTION_ARCHITECT_ASSOCIATE', 'Solution Architect Associate', 'Solution Architect Associate', 'Solution Architecture', p_actor_user_id);

    SELECT COALESCE(SUM(projectpulse055d4d_numeric_or_zero(value->>'extendedAmount')), 0)
    INTO v_total_cost
    FROM jsonb_array_elements(CASE WHEN jsonb_typeof(v_review->'rates') = 'array' THEN v_review->'rates' ELSE '[]'::jsonb END)
    WHERE projectpulse055d4d_bool_or_default(value->>'include', TRUE) = TRUE;

    IF v_total_cost = 0 THEN
        v_total_cost := projectpulse055d4d_numeric_or_zero(v_review->>'projectListPrice');
    END IF;

    SELECT COALESCE(SUM(projectpulse055d4d_numeric_or_zero(value->>'laborListPrice')), 0)
    INTO v_pm_cost
    FROM jsonb_array_elements(CASE WHEN jsonb_typeof(v_review->'tasks') = 'array' THEN v_review->'tasks' ELSE '[]'::jsonb END)
    WHERE projectpulse055d4d_bool_or_default(value->>'include', TRUE) = TRUE
      AND lower(coalesce(value->>'phase','') || ' ' || coalesce(value->>'taskName','') || ' ' || coalesce(value->>'engineeringRole','')) LIKE ANY (
          ARRAY['%project oversight%','%project management%','%project manager%','%project coord%','%pm%']
      );

    v_engineering_cost := GREATEST(v_total_cost - v_pm_cost, 0);

    SELECT status INTO v_project_status
    FROM projects
    WHERE status IS NOT NULL AND btrim(status) <> ''
    ORDER BY CASE lower(status)
        WHEN 'active' THEN 1
        WHEN 'planning' THEN 2
        WHEN 'open' THEN 3
        WHEN 'new' THEN 4
        ELSE 50
    END, created_at DESC
    LIMIT 1;

    v_project_status := coalesce(NULLIF(v_project_status, ''), 'active');

    SELECT utilization_bucket, work_task_category, billing_classification, utilization_classification
    INTO v_task_utilization_bucket, v_task_work_category, v_task_billing_classification, v_task_utilization_classification
    FROM project_tasks
    WHERE billable = TRUE
    ORDER BY created_at DESC
    LIMIT 1;

    v_task_utilization_bucket := coalesce(NULLIF(v_task_utilization_bucket, ''), 'billable');
    v_task_work_category := coalesce(NULLIF(v_task_work_category, ''), 'project_task');
    v_task_billing_classification := coalesce(NULLIF(v_task_billing_classification, ''), 'billable');
    v_task_utilization_classification := coalesce(NULLIF(v_task_utilization_classification, ''), 'billable_utilization');

    SELECT rate_card_type, status, source_system
    INTO v_rate_card_type, v_rate_card_status, v_rate_card_source_system
    FROM work_rate_cards
    ORDER BY created_at DESC
    LIMIT 1;

    v_rate_card_type := coalesce(NULLIF(v_rate_card_type, ''), 'standard');
    v_rate_card_status := coalesce(NULLIF(v_rate_card_status, ''), 'active');
    v_rate_card_source_system := coalesce(NULLIF(v_rate_card_source_system, ''), 'Work Register GSD Intake');

    v_project_id := gen_random_uuid();
    v_project_code := 'WR-' || to_char(NOW(), 'YYYYMMDD') || '-' || upper(substr(replace(p_intake_package_id::text, '-', ''), 1, 6));

    INSERT INTO projects (
        project_id, client_id, project_code, project_name, project_description,
        project_manager_user_id, status, start_date, end_date, billable,
        created_at, updated_at, planned_engineering_cost, planned_pm_cost,
        planned_total_project_cost, account_executive_user_id, solution_architect_user_id
    )
    VALUES (
        v_project_id, v_customer_id, v_project_code, v_project_name,
        'Created from Work Register intake. Work type: ' || v_requested_work_type || '. Contract type: ' || v_contract_type || '.',
        v_pm_user_id, v_project_status, CURRENT_DATE, NULL,
        CASE WHEN lower(v_requested_work_type) = 'presales' THEN FALSE ELSE TRUE END,
        NOW(), NOW(), v_engineering_cost, v_pm_cost, v_total_cost,
        v_ae_user_id, v_sa_user_id
    );

    INSERT INTO work_register_project_metadata (
        project_id, work_register_intake_package_id, requested_work_type,
        contract_type, gsd_template_family, sow_signed_date, intake_reason,
        project_list_price, pm_hours, engineering_hours, travel_hours,
        metadata_json, created_by_user_id
    )
    VALUES (
        v_project_id, p_intake_package_id, v_requested_work_type,
        v_contract_type, v_template_family, v_sow_signed_date, v_intake_reason,
        v_total_cost,
        projectpulse055d4d_numeric_or_zero(v_review->>'pmHours'),
        projectpulse055d4d_numeric_or_zero(v_review->>'engineeringHours'),
        projectpulse055d4d_numeric_or_zero(v_review->>'travelHours'),
        v_review, p_actor_user_id
    );

    IF v_ae_user_id IS NOT NULL THEN
        INSERT INTO work_register_project_stakeholders (project_id, stakeholder_role, user_id, display_name_snapshot, email_snapshot, created_by_user_id)
        SELECT v_project_id, 'Account Executive', user_id, display_name, email, p_actor_user_id FROM app_users WHERE user_id = v_ae_user_id;
        v_stakeholder_count := v_stakeholder_count + 1;
    END IF;

    IF v_sa_user_id IS NOT NULL THEN
        INSERT INTO work_register_project_stakeholders (project_id, stakeholder_role, user_id, display_name_snapshot, email_snapshot, created_by_user_id)
        SELECT v_project_id, 'Solution Architect', user_id, display_name, email, p_actor_user_id FROM app_users WHERE user_id = v_sa_user_id;
        v_stakeholder_count := v_stakeholder_count + 1;
    END IF;

    IF v_saa_user_id IS NOT NULL THEN
        INSERT INTO work_register_project_stakeholders (project_id, stakeholder_role, user_id, display_name_snapshot, email_snapshot, created_by_user_id)
        SELECT v_project_id, 'Solution Architect Associate', user_id, display_name, email, p_actor_user_id FROM app_users WHERE user_id = v_saa_user_id;
        v_stakeholder_count := v_stakeholder_count + 1;
    END IF;

    IF v_pc_user_id IS NOT NULL THEN
        INSERT INTO work_register_project_stakeholders (project_id, stakeholder_role, user_id, display_name_snapshot, email_snapshot, created_by_user_id)
        SELECT v_project_id, 'Project Coordinator / PM Team', user_id, display_name, email, p_actor_user_id FROM app_users WHERE user_id = v_pc_user_id;
        v_stakeholder_count := v_stakeholder_count + 1;
    END IF;

    BEGIN
        v_rate_card_id := gen_random_uuid();

        INSERT INTO work_rate_cards (
            rate_card_id, rate_card_code, rate_card_name, rate_card_type,
            client_id, customer_name_snapshot, status, effective_start_date,
            effective_end_date, source_system, description, is_system_seeded,
            created_by_user_id, updated_by_user_id, created_at, updated_at
        )
        VALUES (
            v_rate_card_id,
            'GSD-' || upper(substr(replace(p_intake_package_id::text, '-', ''), 1, 12)),
            'GSD Rate Snapshot - ' || v_project_code,
            v_rate_card_type,
            v_customer_id,
            coalesce(NULLIF(v_review->>'customerName', ''), v_pkg.customer_hint, ''),
            v_rate_card_status,
            CURRENT_DATE,
            NULL,
            v_rate_card_source_system,
            'Immutable rate snapshot from reviewed GSD intake.',
            FALSE,
            p_actor_user_id,
            p_actor_user_id,
            NOW(),
            NOW()
        );

        FOR v_rate IN
            SELECT value, ordinality
            FROM jsonb_array_elements(CASE WHEN jsonb_typeof(v_review->'rates') = 'array' THEN v_review->'rates' ELSE '[]'::jsonb END) WITH ORDINALITY
        LOOP
            IF projectpulse055d4d_bool_or_default(v_rate.value->>'include', TRUE) = FALSE THEN CONTINUE; END IF;
            v_rate_idx := v_rate_idx + 1;

            INSERT INTO work_rate_card_lines (
                rate_line_id, rate_card_id, sku_code, display_name, description,
                labor_category, time_type, unit_type, rate_amount,
                minimum_billing_hours, remote_minimum_hours, onsite_minimum_hours,
                daytime_minimum_hours, afterhours_weekend_holiday_minimum_hours,
                business_hours_text, billable_default, utilization_eligible_default,
                is_emergency, is_travel, override_allowed, is_active, display_order,
                notes, created_by_user_id, updated_by_user_id, created_at, updated_at
            )
            SELECT
                gen_random_uuid(), v_rate_card_id,
                left(coalesce(NULLIF(v_rate.value->>'sku',''), 'GSD-RATE-' || v_rate_idx::text), 120),
                left(coalesce(NULLIF(v_rate.value->>'description',''), NULLIF(v_rate.value->>'sku',''), 'GSD Rate ' || v_rate_idx::text), 200),
                coalesce(NULLIF(v_rate.value->>'description',''), NULLIF(v_rate.value->>'sku',''), 'GSD Rate Snapshot'),
                left(coalesce(NULLIF(v_rate.value->>'description',''), 'GSD'), 120),
                coalesce((SELECT time_type FROM work_rate_card_lines ORDER BY created_at DESC LIMIT 1), 'normal'),
                coalesce((SELECT unit_type FROM work_rate_card_lines ORDER BY created_at DESC LIMIT 1), 'hour'),
                projectpulse055d4d_numeric_or_zero(v_rate.value->>'rate'),
                0,0,0,0,0,'GSD snapshot', TRUE, TRUE, FALSE,
                lower(coalesce(v_rate.value->>'description','') || ' ' || coalesce(v_rate.value->>'sku','')) LIKE '%travel%',
                TRUE, TRUE, v_rate_idx, v_rate.value::text,
                p_actor_user_id, p_actor_user_id, NOW(), NOW();

            v_rate_count := v_rate_count + 1;
        END LOOP;
    EXCEPTION WHEN others THEN
        v_warnings := v_warnings || jsonb_build_array(jsonb_build_object('scope','rate_snapshot','message',SQLERRM));
        v_rate_count := 0;
    END;

    FOR v_task IN
        SELECT value, ordinality
        FROM jsonb_array_elements(CASE WHEN jsonb_typeof(v_review->'tasks') = 'array' THEN v_review->'tasks' ELSE '[]'::jsonb END) WITH ORDINALITY
    LOOP
        IF projectpulse055d4d_bool_or_default(v_task.value->>'include', TRUE) = FALSE THEN CONTINUE; END IF;
        IF btrim(coalesce(v_task.value->>'taskName', v_task.value->>'phase', '')) = '' THEN CONTINUE; END IF;

        v_task_idx := v_task_idx + 1;
        v_task_id := gen_random_uuid();

        INSERT INTO project_tasks (
            task_id, project_id, task_code, task_name, task_description,
            billable, is_active, created_at, updated_at, utilization_bucket,
            utilization_requires_approval, work_task_category, billing_classification,
            utilization_classification, service_request_number, work_task_notes, work_task_template_id
        )
        VALUES (
            v_task_id, v_project_id,
            left('WR-' || upper(substr(replace(v_project_id::text, '-', ''), 1, 6)) || '-' || lpad(v_task_idx::text, 3, '0'), 80),
            left(coalesce(NULLIF(v_task.value->>'taskName',''), NULLIF(v_task.value->>'phase',''), 'Task ' || v_task_idx::text), 240),
            coalesce(v_task.value->>'engineeringRole', '') || ' | Source: ' || coalesce(v_task.value->>'source', 'reviewed_intake'),
            projectpulse055d4d_bool_or_default(v_task.value->>'billable', TRUE),
            TRUE, NOW(), NOW(), v_task_utilization_bucket, FALSE,
            v_task_work_category, v_task_billing_classification, v_task_utilization_classification,
            NULL, v_task.value::text, NULL
        );

        v_task_count := v_task_count + 1;

        FOR v_assignment IN
            SELECT value, ordinality
            FROM jsonb_array_elements(CASE WHEN jsonb_typeof(v_task.value->'assignments') = 'array' THEN v_task.value->'assignments' ELSE '[]'::jsonb END) WITH ORDINALITY
        LOOP
            IF projectpulse055d4d_uuid_or_null(v_assignment.value->>'engineerUserId') IS NULL THEN CONTINUE; END IF;

            INSERT INTO work_register_task_assignment_history (
                work_register_task_assignment_history_id, project_id, task_id_text,
                task_name_snapshot, assigned_user_id, previous_assigned_user_id,
                allocated_hours, billable, utilization_eligible, assignment_status,
                effective_start_date, effective_end_date, change_reason, changed_by_user_id,
                old_value_json, new_value_json, created_at, allocation_percent,
                assignment_role, roster_batch_id, is_primary
            )
            VALUES (
                gen_random_uuid(), v_project_id, v_task_id::text,
                left(coalesce(NULLIF(v_task.value->>'taskName',''), NULLIF(v_task.value->>'phase',''), 'Task ' || v_task_idx::text), 240),
                projectpulse055d4d_uuid_or_null(v_assignment.value->>'engineerUserId'),
                NULL,
                projectpulse055d4d_numeric_or_zero(v_assignment.value->>'hours'),
                projectpulse055d4d_bool_or_default(v_task.value->>'billable', TRUE),
                TRUE, 'active', CURRENT_DATE, NULL,
                'Created from Work Register intake final save.',
                p_actor_user_id,
                NULL,
                jsonb_build_object('source','work_register_intake_final_save','intakePackageId',p_intake_package_id,'task',v_task.value,'assignment',v_assignment.value),
                NOW(),
                projectpulse055d4d_numeric_or_zero(v_assignment.value->>'allocationPercent'),
                CASE WHEN lower(coalesce(v_task.value->>'phase','') || ' ' || coalesce(v_task.value->>'taskName','') || ' ' || coalesce(v_task.value->>'engineeringRole','')) LIKE '%project oversight%' THEN 'project_management' ELSE 'engineer' END, gen_random_uuid(),
                projectpulse055d4d_bool_or_default(v_assignment.value->>'isPrimary', v_assignment.ordinality = 1)
            );

            v_assignment_count := v_assignment_count + 1;
        END LOOP;
    END LOOP;

    INSERT INTO work_register_project_documents (
        project_id, work_register_intake_package_id, work_register_intake_document_id,
        document_type, original_file_name, stored_file_path, content_type,
        file_size_bytes, created_by_user_id, document_routing_status
    )
    SELECT
        v_project_id, work_register_intake_package_id, work_register_intake_document_id,
        document_type, original_file_name, stored_file_path, content_type,
        file_size_bytes, p_actor_user_id, 'linked_pending_customer_folder_copy'
    FROM work_register_intake_documents
    WHERE work_register_intake_package_id = p_intake_package_id;

    GET DIAGNOSTICS v_document_count = ROW_COUNT;

    v_commit_json := jsonb_build_object(
        'projectId', v_project_id,
        'projectCode', v_project_code,
        'projectName', v_project_name,
        'customerId', v_customer_id,
        'contractType', v_contract_type,
        'requestedWorkType', v_requested_work_type,
        'gsdTemplateFamily', v_template_family,
        'intakeReason', v_intake_reason,
        'sowSignedDate', v_sow_signed_date,
        'tasksCreated', v_task_count,
        'assignmentsCreated', v_assignment_count,
        'ratesSnapshotted', v_rate_count,
        'documentsLinked', v_document_count,
        'stakeholdersLinked', v_stakeholder_count,
        'warnings', v_warnings
    );

    INSERT INTO work_register_intake_commits (
        work_register_intake_package_id, project_id, project_code, committed_by_user_id, commit_summary_json
    )
    VALUES (p_intake_package_id, v_project_id, v_project_code, p_actor_user_id, v_commit_json);

    BEGIN
        UPDATE work_register_intake_packages
        SET intake_status = 'committed_to_work_register',
            review_status = 'committed',
            notes = v_intake_reason,
            updated_at = NOW()
        WHERE work_register_intake_package_id = p_intake_package_id;
    EXCEPTION WHEN others THEN
        v_warnings := v_warnings || jsonb_build_array(jsonb_build_object('scope','intake_status_update','message',SQLERRM));
    END;

    INSERT INTO work_register_change_history (
        work_register_change_history_id, source_table, work_id, action,
        change_summary, changed_fields_csv, changed_by_user_id,
        old_value_json, new_value_json, changed_at
    )
    VALUES (
        gen_random_uuid(), 'work_register_intake_packages', v_project_id,
        'intake_final_saved_to_work_register',
        'Created Work Register project ' || v_project_code || '. Tasks: ' || v_task_count || '. Assignments: ' || v_assignment_count || '.',
        'project,tasks,assignments,rates,documents,stakeholders,sow_signed_date',
        p_actor_user_id,
        jsonb_build_object('intakePackageId', p_intake_package_id),
        v_commit_json,
        NOW()
    );

    RETURN jsonb_build_object(
        'status','committed',
        'message','Created Work Register project ' || v_project_code || '. Tasks: ' || v_task_count || '. Assignments: ' || v_assignment_count || '.',
        'projectId', v_project_id,
        'projectCode', v_project_code,
        'projectName', v_project_name,
        'taskCount', v_task_count,
        'assignmentCount', v_assignment_count,
        'rateCount', v_rate_count,
        'documentCount', v_document_count,
        'stakeholderCount', v_stakeholder_count,
        'warnings', v_warnings
    );

EXCEPTION WHEN others THEN
    GET STACKED DIAGNOSTICS
        v_sqlstate = RETURNED_SQLSTATE,
        v_message = MESSAGE_TEXT,
        v_detail = PG_EXCEPTION_DETAIL,
        v_hint = PG_EXCEPTION_HINT,
        v_context = PG_EXCEPTION_CONTEXT;

    RETURN jsonb_build_object(
        'status','database_error',
        'message',v_message,
        'sqlState',v_sqlstate,
        'detail',coalesce(v_detail,''),
        'hint',coalesce(v_hint,''),
        'context',coalesce(v_context,''),
        'intakePackageId',p_intake_package_id
    );
END;
$$;
