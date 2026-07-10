-- 055D.4L
-- Keep @ussignal.cloud placeholder users guarded.
-- Allow creation only from controlled Work Register workflows:
-- 1. Intake final save
-- 2. Work Register project edit/update route

CREATE OR REPLACE FUNCTION projectpulse_restrict_manual_cloud_domain_user_insert()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_email text;
    v_source_provider text;
    v_entra_object_id text;
    v_login_enabled boolean;
    v_allow_intake text;
    v_allow_work_register_update text;
BEGIN
    v_email := lower(btrim(coalesce(NEW.email, '')));
    v_source_provider := lower(btrim(coalesce(NEW.source_provider, '')));
    v_entra_object_id := lower(btrim(coalesce(NEW.entra_object_id, '')));
    v_login_enabled := coalesce(NEW.login_enabled, FALSE);
    v_allow_intake := lower(coalesce(current_setting('projectpulse.allow_intake_temp_cloud_user', true), ''));
    v_allow_work_register_update := lower(coalesce(current_setting('projectpulse.allow_work_register_temp_cloud_user', true), ''));

    IF v_email = '' THEN
        RETURN NEW;
    END IF;

    IF v_email LIKE '%@ussignal.com' THEN
        IF v_source_provider LIKE '%entra%' THEN
            RETURN NEW;
        END IF;

        RAISE EXCEPTION 'Manual creation of @ussignal.com users is blocked. Import this user through production Entra sync.';
    END IF;

    IF v_email LIKE '%@ussignal.cloud' THEN
        IF (v_allow_intake = 'true' OR v_allow_work_register_update = 'true')
           AND v_source_provider IN ('work_register_intake_temp_cloud', 'work_register_project_edit_temp_cloud')
           AND v_login_enabled = FALSE
           AND v_entra_object_id LIKE 'work-register-%temp-cloud:%'
        THEN
            RETURN NEW;
        END IF;

        IF v_source_provider LIKE '%entra%' THEN
            RETURN NEW;
        END IF;

        RAISE EXCEPTION 'Manual @ussignal.cloud users are blocked. Only non-login Work Register placeholder stakeholders may be created through guarded Work Register workflows.';
    END IF;

    IF v_email LIKE '%@ussignal.local' THEN
        RETURN NEW;
    END IF;

    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4l_assert_project_edit_temp_cloud_guard()
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM set_config('projectpulse.allow_work_register_temp_cloud_user', 'true', true);
END;
$$;
