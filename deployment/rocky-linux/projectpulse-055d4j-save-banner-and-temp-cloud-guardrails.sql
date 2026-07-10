-- 055D.4J
-- Guarded @ussignal.cloud placeholder users and PTC notification queue.

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS work_register_temp_cloud_user_notifications (
    work_register_temp_cloud_user_notification_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id uuid REFERENCES projects(project_id),
    work_register_intake_package_id uuid REFERENCES work_register_intake_packages(work_register_intake_package_id),
    project_code text NOT NULL DEFAULT '',
    project_name text NOT NULL DEFAULT '',
    stakeholder_role text NOT NULL DEFAULT '',
    stakeholder_display_name text NOT NULL DEFAULT '',
    stakeholder_email text NOT NULL DEFAULT '',
    notification_recipients text NOT NULL DEFAULT '',
    notification_subject text NOT NULL DEFAULT '',
    notification_body text NOT NULL DEFAULT '',
    notification_status text NOT NULL DEFAULT 'pending',
    notification_error text NOT NULL DEFAULT '',
    created_by_user_id uuid REFERENCES app_users(user_id),
    created_at timestamptz NOT NULL DEFAULT NOW(),
    sent_at timestamptz
);

CREATE INDEX IF NOT EXISTS idx_work_register_temp_cloud_user_notifications_project
    ON work_register_temp_cloud_user_notifications(project_id);

CREATE INDEX IF NOT EXISTS idx_work_register_temp_cloud_user_notifications_status
    ON work_register_temp_cloud_user_notifications(notification_status);

CREATE OR REPLACE FUNCTION projectpulse_restrict_manual_cloud_domain_user_insert()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_email text;
    v_source_provider text;
    v_entra_object_id text;
    v_login_enabled boolean;
    v_allow_temp_cloud text;
BEGIN
    v_email := lower(btrim(coalesce(NEW.email, '')));
    v_source_provider := lower(btrim(coalesce(NEW.source_provider, '')));
    v_entra_object_id := lower(btrim(coalesce(NEW.entra_object_id, '')));
    v_login_enabled := coalesce(NEW.login_enabled, FALSE);
    v_allow_temp_cloud := lower(coalesce(current_setting('projectpulse.allow_intake_temp_cloud_user', true), ''));

    IF v_email = '' THEN
        RETURN NEW;
    END IF;

    -- Real @ussignal.com users remain Entra-only.
    IF v_email LIKE '%@ussignal.com' THEN
        IF v_source_provider LIKE '%entra%' THEN
            RETURN NEW;
        END IF;

        RAISE EXCEPTION 'Manual creation of @ussignal.com users is blocked. Import this user through production Entra sync.';
    END IF;

    -- Temporary cloud placeholders are allowed only inside the guarded Work Register intake save path.
    IF v_email LIKE '%@ussignal.cloud' THEN
        IF v_allow_temp_cloud = 'true'
           AND v_source_provider = 'work_register_intake_temp_cloud'
           AND v_login_enabled = FALSE
           AND v_entra_object_id LIKE 'work-register-intake-temp-cloud:%'
        THEN
            RETURN NEW;
        END IF;

        IF v_source_provider LIKE '%entra%' THEN
            RETURN NEW;
        END IF;

        RAISE EXCEPTION 'Manual @ussignal.cloud users are blocked. Only non-login Work Register intake placeholder stakeholders may be created through the guarded intake save workflow.';
    END IF;

    -- Local/dev users remain allowed.
    IF v_email LIKE '%@ussignal.local' THEN
        RETURN NEW;
    END IF;

    RETURN NEW;
END;
$$;

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

CREATE OR REPLACE FUNCTION projectpulse055d4d_effective_stakeholder_role_code(p_role_code text)
RETURNS text
LANGUAGE sql
AS $$
    SELECT CASE upper(coalesce(p_role_code, ''))
        WHEN 'ACCOUNT_EXECUTIVE' THEN 'SALES'
        WHEN 'SOLUTION_ARCHITECT' THEN 'SOLUTION_ARCHITECT'
        WHEN 'SOLUTION_ARCHITECT_ASSOCIATE' THEN 'INSIDE_SALES'
        ELSE upper(coalesce(p_role_code, 'STAKEHOLDER'))
    END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4d_effective_stakeholder_role_name(p_role_code text, p_role_name text)
RETURNS text
LANGUAGE sql
AS $$
    SELECT CASE upper(coalesce(p_role_code, ''))
        WHEN 'ACCOUNT_EXECUTIVE' THEN 'Sales'
        WHEN 'SOLUTION_ARCHITECT' THEN 'Solution Architect'
        WHEN 'SOLUTION_ARCHITECT_ASSOCIATE' THEN 'Inside Sales'
        ELSE coalesce(nullif(p_role_name, ''), 'Stakeholder')
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
    v_effective_role_code text;
    v_effective_role_name text;
BEGIN
    v_display_name := btrim(coalesce(p_display_name, ''));

    IF v_display_name = '' THEN
        RETURN NULL;
    END IF;

    v_ussignal_email := projectpulse055d4d_email_from_name(v_display_name);
    v_temp_email := projectpulse055d4d_temp_cloud_email_from_name(v_display_name);

    v_effective_role_code := projectpulse055d4d_effective_stakeholder_role_code(p_role_code);
    v_effective_role_name := projectpulse055d4d_effective_stakeholder_role_name(p_role_code, p_role_name);

    -- Always prefer existing Entra / real @ussignal.com account.
    SELECT user_id
    INTO v_user_id
    FROM app_users
    WHERE lower(email) = lower(v_ussignal_email)
       OR lower(display_name) = lower(v_display_name)
    ORDER BY
        CASE WHEN lower(email) = lower(v_ussignal_email) THEN 0 ELSE 1 END,
        is_active DESC,
        updated_at DESC
    LIMIT 1;

    IF v_user_id IS NULL THEN
        -- Reuse prior temp account if one already exists.
        SELECT user_id
        INTO v_user_id
        FROM app_users
        WHERE lower(email) = lower(v_temp_email)
        ORDER BY is_active DESC, updated_at DESC
        LIMIT 1;
    END IF;

    IF v_user_id IS NULL THEN
        v_user_id := gen_random_uuid();

        -- Guarded insert: trigger only allows this while this local setting is true.
        PERFORM set_config('projectpulse.allow_intake_temp_cloud_user', 'true', true);

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
            v_effective_role_name,
            v_effective_role_name,
            TRUE,
            NOW(),
            NOW(),
            'work_register_intake_temp_cloud',
            v_effective_role_name,
            NULL,
            NULL,
            FALSE,
            v_effective_role_name
        );
    ELSE
        UPDATE app_users
        SET display_name = COALESCE(NULLIF(display_name, ''), v_display_name),
            job_title = COALESCE(NULLIF(job_title, ''), v_effective_role_name),
            department = COALESCE(NULLIF(department, ''), v_effective_role_name),
            department_name = COALESCE(NULLIF(department_name, ''), v_effective_role_name),
            team_name = COALESCE(NULLIF(team_name, ''), v_effective_role_name),
            login_enabled = CASE
                WHEN lower(email) LIKE '%@ussignal.cloud' THEN FALSE
                ELSE login_enabled
            END,
            updated_at = NOW()
        WHERE user_id = v_user_id;
    END IF;

    SELECT app_role_id
    INTO v_app_role_id
    FROM app_roles
    WHERE role_code = v_effective_role_code
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
            v_effective_role_code,
            v_effective_role_name,
            'Created or linked by Work Register intake final save for stakeholder tracking.',
            FALSE,
            TRUE,
            900,
            NOW(),
            NOW()
        );
    ELSE
        UPDATE app_roles
        SET role_name = v_effective_role_name,
            is_active = TRUE,
            updated_at = NOW()
        WHERE app_role_id = v_app_role_id;
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
            'Automatic stakeholder role assigned by Work Register intake final save.',
            TRUE,
            NOW(),
            NOW()
        );
    END IF;

    RETURN v_user_id;
END;
$$;
