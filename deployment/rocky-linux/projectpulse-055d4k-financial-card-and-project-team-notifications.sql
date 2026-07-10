CREATE EXTENSION IF NOT EXISTS pgcrypto;

ALTER TABLE work_register_project_metadata
    ADD COLUMN IF NOT EXISTS requested_work_type text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS contract_type text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS gsd_template_family text NOT NULL DEFAULT 'standard',
    ADD COLUMN IF NOT EXISTS sow_signed_date date,
    ADD COLUMN IF NOT EXISTS intake_reason text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS project_list_price numeric NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS pm_hours numeric NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS engineering_hours numeric NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS travel_hours numeric NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT NOW();

CREATE TABLE IF NOT EXISTS work_register_project_team_notifications (
    work_register_project_team_notification_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id uuid REFERENCES projects(project_id),
    work_register_intake_package_id uuid REFERENCES work_register_intake_packages(work_register_intake_package_id),
    recipient_user_id uuid REFERENCES app_users(user_id),
    recipient_email text NOT NULL DEFAULT '',
    recipient_display_name text NOT NULL DEFAULT '',
    recipient_project_role text NOT NULL DEFAULT '',
    project_code text NOT NULL DEFAULT '',
    project_name text NOT NULL DEFAULT '',
    notification_subject text NOT NULL DEFAULT '',
    notification_body text NOT NULL DEFAULT '',
    notification_status text NOT NULL DEFAULT 'pending',
    notification_error text NOT NULL DEFAULT '',
    created_by_user_id uuid REFERENCES app_users(user_id),
    created_at timestamptz NOT NULL DEFAULT NOW(),
    sent_at timestamptz
);

CREATE TABLE IF NOT EXISTS customer_billing_balances (
    customer_billing_balance_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    client_id uuid NOT NULL,
    customer_name_snapshot text NOT NULL DEFAULT '',
    balance_type text NOT NULL DEFAULT 'TM',
    contract_reference text NOT NULL DEFAULT '',
    beginning_hours numeric NOT NULL DEFAULT 0,
    purchased_hours numeric NOT NULL DEFAULT 0,
    consumed_hours numeric NOT NULL DEFAULT 0,
    remaining_hours numeric NOT NULL DEFAULT 0,
    beginning_amount numeric NOT NULL DEFAULT 0,
    purchased_amount numeric NOT NULL DEFAULT 0,
    consumed_amount numeric NOT NULL DEFAULT 0,
    remaining_amount numeric NOT NULL DEFAULT 0,
    effective_start_date date,
    effective_end_date date,
    status text NOT NULL DEFAULT 'active',
    notes text NOT NULL DEFAULT '',
    created_by_user_id uuid REFERENCES app_users(user_id),
    created_at timestamptz NOT NULL DEFAULT NOW(),
    updated_at timestamptz NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS project_invoice_events (
    project_invoice_event_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id uuid NOT NULL REFERENCES projects(project_id),
    invoice_event_type text NOT NULL DEFAULT 'partial_invoice',
    invoice_status text NOT NULL DEFAULT 'draft',
    invoice_basis text NOT NULL DEFAULT '',
    requested_amount numeric NOT NULL DEFAULT 0,
    requested_hours numeric NOT NULL DEFAULT 0,
    billed_amount numeric NOT NULL DEFAULT 0,
    billed_hours numeric NOT NULL DEFAULT 0,
    invoice_reference text NOT NULL DEFAULT '',
    requested_by_user_id uuid REFERENCES app_users(user_id),
    approved_by_user_id uuid REFERENCES app_users(user_id),
    requested_at timestamptz NOT NULL DEFAULT NOW(),
    approved_at timestamptz,
    notes text NOT NULL DEFAULT '',
    event_json jsonb NOT NULL DEFAULT '{}'::jsonb
);

CREATE OR REPLACE FUNCTION projectpulse055d4k_normalize_contract_type(value text)
RETURNS text
LANGUAGE sql
AS $$
    SELECT CASE
        WHEN lower(coalesce(value, '')) IN ('fp', 'fixed price', 'fixedprice') THEN 'FP'
        WHEN lower(coalesce(value, '')) IN ('tm', 't&m', 'time and material', 'time & material', 'timeandmaterial') THEN 'T&M'
        ELSE coalesce(nullif(value, ''), 'Not set')
    END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4k_intake_reason(work_type text)
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

CREATE OR REPLACE FUNCTION projectpulse055d4k_numeric_from_json(value text)
RETURNS numeric
LANGUAGE plpgsql
AS $$
DECLARE
    cleaned text;
BEGIN
    cleaned := regexp_replace(coalesce(value, ''), '[^0-9\.\-]+', '', 'g');
    IF cleaned IS NULL OR cleaned = '' OR cleaned = '-' OR cleaned = '.' THEN
        RETURN 0;
    END IF;
    RETURN cleaned::numeric;
EXCEPTION WHEN others THEN
    RETURN 0;
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4k_sync_project_financials(p_intake_package_id uuid)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    v_commit record;
    v_pkg record;
    v_review jsonb;
    v_contract_type text;
    v_work_type text;
    v_sow_signed_date date;
    v_total_cost numeric := 0;
    v_pm_cost numeric := 0;
    v_engineering_cost numeric := 0;
    v_pm_hours numeric := 0;
    v_engineering_hours numeric := 0;
    v_travel_hours numeric := 0;
    v_intake_reason text;
BEGIN
    SELECT *
    INTO v_commit
    FROM work_register_intake_commits
    WHERE work_register_intake_package_id = p_intake_package_id
    ORDER BY committed_at DESC
    LIMIT 1;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    SELECT *
    INTO v_pkg
    FROM work_register_intake_packages
    WHERE work_register_intake_package_id = p_intake_package_id;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    v_review := coalesce(v_pkg.reviewed_json, '{}'::jsonb);
    v_work_type := coalesce(nullif(v_review->>'requestedWorkType', ''), nullif(v_pkg.requested_work_type, ''), 'Project');
    v_contract_type := projectpulse055d4k_normalize_contract_type(coalesce(nullif(v_review->>'contractType', ''), nullif(v_pkg.contract_type, '')));

    BEGIN
        v_sow_signed_date := NULLIF(v_review->>'sowSignedDate', '')::date;
    EXCEPTION WHEN others THEN
        v_sow_signed_date := NULL;
    END;

    SELECT coalesce(sum(projectpulse055d4k_numeric_from_json(value->>'extendedAmount')), 0)
    INTO v_total_cost
    FROM jsonb_array_elements(CASE WHEN jsonb_typeof(v_review->'rates') = 'array' THEN v_review->'rates' ELSE '[]'::jsonb END)
    WHERE coalesce(value->>'include', 'true') <> 'false';

    IF v_total_cost = 0 THEN
        SELECT coalesce(sum(projectpulse055d4k_numeric_from_json(value->>'laborListPrice')), 0)
        INTO v_total_cost
        FROM jsonb_array_elements(CASE WHEN jsonb_typeof(v_review->'tasks') = 'array' THEN v_review->'tasks' ELSE '[]'::jsonb END)
        WHERE coalesce(value->>'include', 'true') <> 'false';
    END IF;

    IF v_total_cost = 0 THEN
        v_total_cost := projectpulse055d4k_numeric_from_json(v_review->>'projectListPrice');
    END IF;

    SELECT coalesce(sum(projectpulse055d4k_numeric_from_json(value->>'laborListPrice')), 0),
           coalesce(sum(projectpulse055d4k_numeric_from_json(value->>'pmHours')), 0)
    INTO v_pm_cost, v_pm_hours
    FROM jsonb_array_elements(CASE WHEN jsonb_typeof(v_review->'tasks') = 'array' THEN v_review->'tasks' ELSE '[]'::jsonb END)
    WHERE lower(coalesce(value->>'phase','') || ' ' || coalesce(value->>'taskName','') || ' ' || coalesce(value->>'engineeringRole',''))
          LIKE ANY (ARRAY['%project oversight%','%project manager%','%project management%','%project coord%','%pm%']);

    SELECT coalesce(sum(projectpulse055d4k_numeric_from_json(value->>'regularHours') + projectpulse055d4k_numeric_from_json(value->>'overtimeHours')), 0),
           coalesce(sum(projectpulse055d4k_numeric_from_json(value->>'travelHours')), 0)
    INTO v_engineering_hours, v_travel_hours
    FROM jsonb_array_elements(CASE WHEN jsonb_typeof(v_review->'tasks') = 'array' THEN v_review->'tasks' ELSE '[]'::jsonb END)
    WHERE coalesce(value->>'include', 'true') <> 'false';

    v_engineering_cost := greatest(v_total_cost - coalesce(v_pm_cost, 0), 0);
    v_intake_reason := projectpulse055d4k_intake_reason(v_work_type);

    UPDATE projects
    SET planned_total_project_cost = v_total_cost,
        planned_pm_cost = coalesce(v_pm_cost, 0),
        planned_engineering_cost = v_engineering_cost,
        project_description = trim(both from concat_ws(
            ' ',
            nullif(project_description, ''),
            'Contract type: ' || v_contract_type || '.',
            CASE WHEN v_sow_signed_date IS NOT NULL THEN 'SOW signed date: ' || v_sow_signed_date::text || '.' ELSE '' END,
            v_intake_reason
        )),
        updated_at = NOW()
    WHERE project_id = v_commit.project_id;

    INSERT INTO work_register_project_metadata (
        project_id,
        work_register_intake_package_id,
        requested_work_type,
        contract_type,
        gsd_template_family,
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
        v_commit.project_id,
        p_intake_package_id,
        v_work_type,
        v_contract_type,
        coalesce(nullif(v_review->>'gsdTemplateFamily', ''), 'standard'),
        v_sow_signed_date,
        v_intake_reason,
        v_total_cost,
        coalesce(v_pm_hours, 0),
        coalesce(v_engineering_hours, 0),
        coalesce(v_travel_hours, 0),
        v_review,
        NOW()
    )
    ON CONFLICT (project_id) DO UPDATE
    SET requested_work_type = EXCLUDED.requested_work_type,
        contract_type = EXCLUDED.contract_type,
        gsd_template_family = EXCLUDED.gsd_template_family,
        sow_signed_date = EXCLUDED.sow_signed_date,
        intake_reason = EXCLUDED.intake_reason,
        project_list_price = EXCLUDED.project_list_price,
        pm_hours = EXCLUDED.pm_hours,
        engineering_hours = EXCLUDED.engineering_hours,
        travel_hours = EXCLUDED.travel_hours,
        metadata_json = EXCLUDED.metadata_json,
        updated_at = NOW();
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4k_queue_project_team_notifications(p_intake_package_id uuid)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    v_commit record;
    v_project record;
    v_recipient record;
    v_subject text;
    v_body text;
BEGIN
    SELECT c.*, i.reviewed_json
    INTO v_commit
    FROM work_register_intake_commits c
    JOIN work_register_intake_packages i
      ON i.work_register_intake_package_id = c.work_register_intake_package_id
    WHERE c.work_register_intake_package_id = p_intake_package_id
    ORDER BY c.committed_at DESC
    LIMIT 1;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    SELECT *
    INTO v_project
    FROM projects
    WHERE project_id = v_commit.project_id;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    v_subject := 'ProjectPulse assignment: ' || coalesce(v_project.project_code, '') || ' - ' || coalesce(v_project.project_name, '');

    FOR v_recipient IN
        WITH recipients AS (
            SELECT u.user_id, u.email, u.display_name, 'Project Manager' AS project_role
            FROM app_users u
            WHERE u.user_id = v_project.project_manager_user_id

            UNION

            SELECT u.user_id, u.email, u.display_name, s.stakeholder_role
            FROM work_register_project_stakeholders s
            JOIN app_users u ON u.user_id = s.user_id
            WHERE s.project_id = v_project.project_id
              AND lower(s.stakeholder_role) LIKE ANY (ARRAY['%project coordinator%','%project team coordinator%'])

            UNION

            SELECT DISTINCT u.user_id, u.email, u.display_name, 'Engineer' AS project_role
            FROM work_register_task_assignment_history a
            JOIN app_users u ON u.user_id = a.assigned_user_id
            WHERE a.project_id = v_project.project_id
              AND coalesce(a.assignment_status, '') = 'active'
        )
        SELECT *
        FROM recipients
        WHERE email IS NOT NULL
          AND btrim(email) <> ''
          AND lower(email) NOT LIKE '%@ussignal.cloud'
    LOOP
        v_body := 'You have been assigned to a ProjectPulse work item.' || chr(10) || chr(10) ||
                  'Project: ' || coalesce(v_project.project_code, '') || ' - ' || coalesce(v_project.project_name, '') || chr(10) ||
                  'Role: ' || v_recipient.project_role || chr(10) ||
                  'Contract: ' || coalesce((SELECT contract_type FROM work_register_project_metadata WHERE project_id = v_project.project_id), 'Not set') || chr(10) ||
                  'Planned total: $' || coalesce(v_project.planned_total_project_cost, 0)::text || chr(10) || chr(10) ||
                  'Please review your assignment in ProjectPulse.';

        INSERT INTO work_register_project_team_notifications (
            project_id,
            work_register_intake_package_id,
            recipient_user_id,
            recipient_email,
            recipient_display_name,
            recipient_project_role,
            project_code,
            project_name,
            notification_subject,
            notification_body,
            notification_status,
            created_by_user_id
        )
        SELECT
            v_project.project_id,
            p_intake_package_id,
            v_recipient.user_id,
            v_recipient.email,
            v_recipient.display_name,
            v_recipient.project_role,
            v_project.project_code,
            v_project.project_name,
            v_subject,
            v_body,
            'pending',
            v_commit.committed_by_user_id
        WHERE NOT EXISTS (
            SELECT 1
            FROM work_register_project_team_notifications existing
            WHERE existing.project_id = v_project.project_id
              AND existing.recipient_user_id = v_recipient.user_id
              AND existing.recipient_project_role = v_recipient.project_role
        );
    END LOOP;
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4k_after_intake_commit()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM projectpulse055d4k_sync_project_financials(NEW.work_register_intake_package_id);
    PERFORM projectpulse055d4k_queue_project_team_notifications(NEW.work_register_intake_package_id);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_projectpulse055d4k_after_intake_commit ON work_register_intake_commits;

CREATE TRIGGER trg_projectpulse055d4k_after_intake_commit
AFTER INSERT ON work_register_intake_commits
FOR EACH ROW
EXECUTE FUNCTION projectpulse055d4k_after_intake_commit();

-- Backfill all already-committed intake packages.
SELECT projectpulse055d4k_sync_project_financials(work_register_intake_package_id)
FROM work_register_intake_commits;

SELECT projectpulse055d4k_queue_project_team_notifications(work_register_intake_package_id)
FROM work_register_intake_commits;
