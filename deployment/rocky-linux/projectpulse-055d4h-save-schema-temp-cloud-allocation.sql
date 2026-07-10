-- 055D.4H - Save schema repair, temp @ussignal.cloud stakeholders, allocation stability.

CREATE EXTENSION IF NOT EXISTS pgcrypto;

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
    ADD COLUMN IF NOT EXISTS created_by_user_id uuid REFERENCES app_users(user_id),
    ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT NOW(),
    ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT NOW();

CREATE OR REPLACE FUNCTION projectpulse055d4d_temp_cloud_email_from_name(display_name text)
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

    IF cleaned = '' THEN
        RETURN '';
    END IF;

    parts := regexp_split_to_array(cleaned, '\s+');

    IF array_length(parts, 1) IS NULL THEN
        RETURN '';
    END IF;

    RETURN parts[1] || '.' || parts[array_length(parts, 1)] || '@ussignal.cloud';
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
    v_ussignal_email text;
    v_temp_email text;
    v_user_id uuid;
    v_app_role_id uuid;
BEGIN
    v_display_name := btrim(coalesce(p_display_name, ''));

    IF v_display_name = '' THEN
        RETURN NULL;
    END IF;

    -- Existing Entra/prod account lookup first.
    v_ussignal_email := projectpulse055d4d_email_from_name(v_display_name);
    v_temp_email := projectpulse055d4d_temp_cloud_email_from_name(v_display_name);

    SELECT user_id
    INTO v_user_id
    FROM app_users
    WHERE lower(email) IN (lower(v_ussignal_email), lower(v_temp_email))
       OR lower(display_name) = lower(v_display_name)
    ORDER BY
        CASE WHEN lower(email) = lower(v_ussignal_email) THEN 0 ELSE 1 END,
        is_active DESC,
        updated_at DESC
    LIMIT 1;

    IF v_user_id IS NULL THEN
        v_user_id := gen_random_uuid();

        INSERT INTO app_users (
            user_id,
            entra_object_id,
            email,
            display_name,
            employee_number,
            job_title,
            department,
            is_active,
            created_at,
            updated_at,
            source_provider,
            department_name,
            office_location,
            manager_email,
            login_enabled,
            team_name
        )
        VALUES (
            v_user_id,
            'work-register-intake-temp-cloud:' || v_user_id::text,
            v_temp_email,
            v_display_name,
            NULL,
            p_job_title,
            p_team_name,
            TRUE,
            NOW(),
            NOW(),
            'work_register_intake_temp_cloud',
            p_team_name,
            NULL,
            NULL,
            FALSE,
            p_team_name
        );
    ELSE
        UPDATE app_users
        SET display_name = COALESCE(NULLIF(display_name, ''), v_display_name),
            job_title = COALESCE(NULLIF(job_title, ''), p_job_title),
            department = COALESCE(NULLIF(department, ''), p_team_name),
            department_name = COALESCE(NULLIF(department_name, ''), p_team_name),
            team_name = COALESCE(NULLIF(team_name, ''), p_team_name),
            is_active = TRUE,
            updated_at = NOW()
        WHERE user_id = v_user_id;
    END IF;

    SELECT app_role_id
    INTO v_app_role_id
    FROM app_roles
    WHERE role_code = p_role_code
    LIMIT 1;

    IF v_app_role_id IS NULL THEN
        v_app_role_id := gen_random_uuid();

        INSERT INTO app_roles (
            app_role_id,
            role_code,
            role_name,
            role_description,
            is_system_role,
            is_active,
            display_order,
            created_at,
            updated_at
        )
        VALUES (
            v_app_role_id,
            p_role_code,
            p_role_name,
            'Created or linked by Work Register intake final save for stakeholder notification routing.',
            FALSE,
            TRUE,
            900,
            NOW(),
            NOW()
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
            app_user_role_assignment_id,
            user_id,
            app_role_id,
            assigned_by_user_id,
            assignment_reason,
            is_active,
            assigned_at,
            updated_at
        )
        VALUES (
            gen_random_uuid(),
            v_user_id,
            v_app_role_id,
            p_actor_user_id,
            'Temporary @ussignal.cloud stakeholder created or linked by Work Register intake final save.',
            TRUE,
            NOW(),
            NOW()
        );
    END IF;

    RETURN v_user_id;
END;
$$;
