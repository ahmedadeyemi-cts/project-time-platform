CREATE EXTENSION IF NOT EXISTS pgcrypto;

ALTER TABLE projects
    ADD COLUMN IF NOT EXISTS contract_type text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS sow_signed_date date,
    ADD COLUMN IF NOT EXISTS project_coordinator_user_id uuid REFERENCES app_users(user_id),
    ADD COLUMN IF NOT EXISTS solution_architect_associate_user_id uuid REFERENCES app_users(user_id),
    ADD COLUMN IF NOT EXISTS work_register_edit_metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb;

ALTER TABLE work_register_project_metadata
    ADD COLUMN IF NOT EXISTS contract_type text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS sow_signed_date date,
    ADD COLUMN IF NOT EXISTS intake_reason text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS project_list_price numeric NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT NOW();

CREATE OR REPLACE FUNCTION projectpulse055d4r1_uuid_or_null(value text)
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

CREATE OR REPLACE FUNCTION projectpulse055d4r1_normalize_contract(value text)
RETURNS text
LANGUAGE sql
AS $fn$
    SELECT CASE
        WHEN lower(coalesce(value, '')) IN ('fp', 'fixed price', 'fixedprice') THEN 'FP'
        WHEN lower(coalesce(value, '')) IN ('tm', 't&m', 'fp', 'fixed price', 'fixedprice') THEN 'FP'
        WHEN lower(coalesce(value, '')) IN ('tm', 't&m', 'time and material', 'time & material', 'timeandmaterial') THEN 'T&M'
        ELSE coalesce(nullif(value, ''), '')
    END;
$fn$;

CREATE OR REPLACE FUNCTION projectpulse055d4r1_payload_value(payload jsonb, VARIADIC keys text[])
RETURNS text
LANGUAGE plpgsql
AS $fn$
DECLARE
    k text;
    w text;
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

    FOREACH w IN ARRAY ARRAY[
        'project',
        'work',
        'item',
        'row',
        'record',
        'selectedProject',
        'selectedWorkRegisterProject',
        'editProject',
        'editingProject',
        'editForm',
        'form',
        'values',
        'payload',
        'setup',
        'setupForm'
    ] LOOP
        IF jsonb_typeof(payload -> w) = 'object' THEN
            FOREACH k IN ARRAY keys LOOP
                v := payload #>> ARRAY[w, k];
                IF v IS NOT NULL AND btrim(v) <> '' THEN
                    RETURN v;
                END IF;
            END LOOP;
        END IF;
    END LOOP;

    RETURN NULL;
END;
$fn$;

CREATE OR REPLACE FUNCTION projectpulse055d4r1_resolve_project_id(payload jsonb)
RETURNS uuid
LANGUAGE plpgsql
AS $fn$
DECLARE
    v_project_id uuid;
    v_id_text text;
    v_project_code text;
    v_project_name text;
BEGIN
    v_id_text := projectpulse055d4r1_payload_value(
        payload,
        'projectId',
        'project_id',
        'id',
        'workId',
        'work_id',
        'workRegisterProjectId',
        'selectedProjectId',
        'selectedWorkRegisterProjectId'
    );

    v_project_id := projectpulse055d4r1_uuid_or_null(v_id_text);

    IF v_project_id IS NOT NULL THEN
        RETURN v_project_id;
    END IF;

    v_project_code := projectpulse055d4r1_payload_value(payload, 'projectCode', 'project_code', 'workCode', 'work_code', 'code');

    IF v_project_code IS NOT NULL THEN
        SELECT p.project_id
        INTO v_project_id
        FROM projects p
        WHERE lower(p.project_code) = lower(v_project_code)
        ORDER BY p.updated_at DESC NULLS LAST, p.created_at DESC NULLS LAST
        LIMIT 1;

        IF v_project_id IS NOT NULL THEN
            RETURN v_project_id;
        END IF;
    END IF;

    v_project_name := projectpulse055d4r1_payload_value(payload, 'projectName', 'project_name', 'name');

    IF v_project_name IS NOT NULL THEN
        SELECT p.project_id
        INTO v_project_id
        FROM projects p
        WHERE lower(p.project_name) = lower(v_project_name)
        ORDER BY p.updated_at DESC NULLS LAST, p.created_at DESC NULLS LAST
        LIMIT 1;

        IF v_project_id IS NOT NULL THEN
            RETURN v_project_id;
        END IF;
    END IF;

    RETURN NULL;
END;
$fn$;

CREATE OR REPLACE FUNCTION projectpulse055d4r1_upsert_stakeholder(
    p_project_id uuid,
    p_role text,
    p_aliases text[],
    p_user_id uuid,
    p_actor_user_id uuid
)
RETURNS void
LANGUAGE plpgsql
AS $fn$
DECLARE
    v_display_name text := '';
    v_email text := '';
    v_row_count integer := 0;
BEGIN
    IF p_user_id IS NULL THEN
        RETURN;
    END IF;

    SELECT coalesce(display_name, ''), coalesce(email, '')
    INTO v_display_name, v_email
    FROM app_users
    WHERE user_id = p_user_id;

    UPDATE work_register_project_stakeholders
    SET user_id = p_user_id,
        display_name_snapshot = v_display_name,
        email_snapshot = v_email,
        source_system = 'work_register_project_edit',
        created_by_user_id = p_actor_user_id,
        created_at = NOW()
    WHERE project_id = p_project_id
      AND lower(stakeholder_role) IN (
          SELECT lower(alias_value)
          FROM unnest(p_aliases || ARRAY[p_role]) AS alias_value
      );

    GET DIAGNOSTICS v_row_count = ROW_COUNT;

    IF v_row_count = 0 THEN
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
    END IF;
END;
$fn$;

CREATE OR REPLACE FUNCTION projectpulse055d4m_json_text(payload jsonb, VARIADIC keys text[])
RETURNS text
LANGUAGE plpgsql
AS $fn$
DECLARE
    value text;
    resolved_project_id uuid;
BEGIN
    value := projectpulse055d4r1_payload_value(payload, VARIADIC keys);

    IF value IS NOT NULL THEN
        RETURN value;
    END IF;

    IF keys && ARRAY[
        'projectId',
        'project_id',
        'id',
        'workId',
        'work_id',
        'workRegisterProjectId',
        'selectedProjectId',
        'selectedWorkRegisterProjectId'
    ] THEN
        resolved_project_id := projectpulse055d4r1_resolve_project_id(payload);

        IF resolved_project_id IS NOT NULL THEN
            RETURN resolved_project_id::text;
        END IF;
    END IF;

    RETURN NULL;
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
    v_pm_user_id uuid;
    v_pc_user_id uuid;
    v_ae_user_id uuid;
    v_sa_user_id uuid;
    v_saa_user_id uuid;
    v_status text;
    v_start_date date;
    v_end_date date;
    v_edit_reason text;
BEGIN
    v_project_id := projectpulse055d4r1_resolve_project_id(p_payload);

    IF v_project_id IS NULL THEN
        RETURN jsonb_build_object(
            'status', 'validation_error',
            'message', 'Project ID is required for Work Register project update.',
            'topLevelKeys', COALESCE((SELECT jsonb_agg(key) FROM jsonb_object_keys(p_payload) key), '[]'::jsonb)
        );
    END IF;

    IF NOT EXISTS (SELECT 1 FROM projects WHERE project_id = v_project_id) THEN
        RETURN jsonb_build_object(
            'status', 'not_found',
            'message', 'Project was not found.',
            'projectId', v_project_id
        );
    END IF;

    v_contract_type := projectpulse055d4r1_normalize_contract(
        projectpulse055d4r1_payload_value(p_payload, 'contractType', 'contract_type', 'contract')
    );

    v_pm_user_id := projectpulse055d4r1_uuid_or_null(
        projectpulse055d4r1_payload_value(p_payload, 'projectManagerUserId', 'project_manager_user_id', 'pmUserId')
    );

    v_pc_user_id := projectpulse055d4r1_uuid_or_null(
        projectpulse055d4r1_payload_value(p_payload, 'projectCoordinatorUserId', 'projectTeamCoordinatorUserId', 'pcUserId', 'ptcUserId')
    );

    v_ae_user_id := projectpulse055d4r1_uuid_or_null(
        projectpulse055d4r1_payload_value(p_payload, 'accountExecutiveUserId', 'account_executive_user_id', 'aeUserId')
    );

    v_sa_user_id := projectpulse055d4r1_uuid_or_null(
        projectpulse055d4r1_payload_value(p_payload, 'solutionArchitectUserId', 'solution_architect_user_id', 'saUserId')
    );

    v_saa_user_id := projectpulse055d4r1_uuid_or_null(
        projectpulse055d4r1_payload_value(p_payload, 'solutionArchitectAssociateUserId', 'saaUserId', 'insideSalesUserId', 'inside_sales_user_id')
    );

    v_status := projectpulse055d4r1_payload_value(p_payload, 'status', 'projectStatus');

    BEGIN
        v_start_date := NULLIF(projectpulse055d4r1_payload_value(p_payload, 'startDate', 'start_date', 'projectStartDate'), '')::date;
    EXCEPTION WHEN others THEN
        v_start_date := NULL;
    END;

    BEGIN
        v_end_date := NULLIF(projectpulse055d4r1_payload_value(p_payload, 'endDate', 'end_date', 'estimatedEndDate', 'projectEndDate'), '')::date;
    EXCEPTION WHEN others THEN
        v_end_date := NULL;
    END;

    BEGIN
        v_sow_signed_date := NULLIF(projectpulse055d4r1_payload_value(p_payload, 'sowSignedDate', 'sow_signed_date'), '')::date;
    EXCEPTION WHEN others THEN
        v_sow_signed_date := NULL;
    END;

    v_edit_reason := coalesce(projectpulse055d4r1_payload_value(p_payload, 'editReason', 'changeReason', 'reason'), '');

    UPDATE projects
    SET contract_type = CASE WHEN v_contract_type <> '' THEN v_contract_type ELSE contract_type END,
        sow_signed_date = coalesce(v_sow_signed_date, sow_signed_date),
        project_manager_user_id = coalesce(v_pm_user_id, project_manager_user_id),
        project_coordinator_user_id = coalesce(v_pc_user_id, project_coordinator_user_id),
        account_executive_user_id = coalesce(v_ae_user_id, account_executive_user_id),
        solution_architect_user_id = coalesce(v_sa_user_id, solution_architect_user_id),
        solution_architect_associate_user_id = coalesce(v_saa_user_id, solution_architect_associate_user_id),
        status = coalesce(nullif(v_status, ''), status),
        start_date = coalesce(v_start_date, start_date),
        end_date = coalesce(v_end_date, end_date),
        work_register_edit_metadata_json = coalesce(work_register_edit_metadata_json, '{}'::jsonb) || p_payload,
        updated_at = NOW()
    WHERE project_id = v_project_id;

    IF v_pc_user_id IS NOT NULL THEN
        PERFORM projectpulse055d4r1_upsert_stakeholder(
            v_project_id,
            'Project Team Coordinator',
            ARRAY['Project Coordinator', 'Project Management Team', 'PTC', 'PC'],
            v_pc_user_id,
            p_actor_user_id
        );
    END IF;

    IF v_ae_user_id IS NOT NULL THEN
        PERFORM projectpulse055d4r1_upsert_stakeholder(
            v_project_id,
            'Account Executive',
            ARRAY['AE', 'Sales'],
            v_ae_user_id,
            p_actor_user_id
        );
    END IF;

    IF v_sa_user_id IS NOT NULL THEN
        PERFORM projectpulse055d4r1_upsert_stakeholder(
            v_project_id,
            'Solution Architect',
            ARRAY['SA'],
            v_sa_user_id,
            p_actor_user_id
        );
    END IF;

    IF v_saa_user_id IS NOT NULL THEN
        PERFORM projectpulse055d4r1_upsert_stakeholder(
            v_project_id,
            'Solution Architect Associate',
            ARRAY['Inside Sales', 'Inside Sales / SAA', 'SAA', 'Solution Architect Associate / Inside Sales'],
            v_saa_user_id,
            p_actor_user_id
        );
    END IF;

    IF v_contract_type <> '' OR v_sow_signed_date IS NOT NULL OR v_edit_reason <> '' THEN
        INSERT INTO work_register_project_metadata (
            project_id,
            contract_type,
            sow_signed_date,
            intake_reason,
            metadata_json,
            updated_at
        )
        VALUES (
            v_project_id,
            v_contract_type,
            v_sow_signed_date,
            v_edit_reason,
            p_payload,
            NOW()
        )
        ON CONFLICT (project_id) DO UPDATE
        SET contract_type = CASE
                WHEN EXCLUDED.contract_type <> '' THEN EXCLUDED.contract_type
                ELSE work_register_project_metadata.contract_type
            END,
            sow_signed_date = coalesce(EXCLUDED.sow_signed_date, work_register_project_metadata.sow_signed_date),
            intake_reason = CASE
                WHEN EXCLUDED.intake_reason <> '' THEN EXCLUDED.intake_reason
                ELSE work_register_project_metadata.intake_reason
            END,
            metadata_json = coalesce(work_register_project_metadata.metadata_json, '{}'::jsonb) || EXCLUDED.metadata_json,
            updated_at = NOW();
    END IF;

    RETURN jsonb_build_object(
        'status', 'updated',
        'message', 'Project edit saved.',
        'projectId', v_project_id,
        'contractType', (SELECT contract_type FROM projects WHERE project_id = v_project_id),
        'sowSignedDate', (SELECT sow_signed_date FROM projects WHERE project_id = v_project_id),
        'insideSalesUserId', (SELECT solution_architect_associate_user_id FROM projects WHERE project_id = v_project_id)
    );

EXCEPTION WHEN others THEN
    RETURN jsonb_build_object(
        'status', 'database_error',
        'message', SQLERRM,
        'sqlState', SQLSTATE,
        'projectId', v_project_id
    );
END;
$fn$;

UPDATE projects p
SET contract_type = m.contract_type,
    sow_signed_date = coalesce(m.sow_signed_date, p.sow_signed_date),
    updated_at = NOW()
FROM work_register_project_metadata m
WHERE m.project_id = p.project_id
  AND coalesce(m.contract_type, '') <> ''
  AND coalesce(p.contract_type, '') = '';

UPDATE projects p
SET solution_architect_associate_user_id = s.user_id,
    updated_at = NOW()
FROM work_register_project_stakeholders s
WHERE s.project_id = p.project_id
  AND s.user_id IS NOT NULL
  AND lower(s.stakeholder_role) IN (
      'solution architect associate',
      'inside sales',
      'inside sales / saa',
      'saa',
      'solution architect associate / inside sales'
  )
  AND p.solution_architect_associate_user_id IS NULL;

UPDATE projects p
SET project_coordinator_user_id = s.user_id,
    updated_at = NOW()
FROM work_register_project_stakeholders s
WHERE s.project_id = p.project_id
  AND s.user_id IS NOT NULL
  AND lower(s.stakeholder_role) IN (
      'project team coordinator',
      'project coordinator',
      'project management team',
      'ptc',
      'pc'
  )
  AND p.project_coordinator_user_id IS NULL;
