-- 055D.4G - Final save fix.
-- Do not manually create @ussignal.com users. Link existing Entra users when present.
-- If missing, preserve AE/SA/SAA notification emails as stakeholder snapshots.

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
BEGIN
    v_display_name := btrim(coalesce(p_display_name, ''));

    IF v_display_name = '' THEN
        RETURN NULL;
    END IF;

    v_email := projectpulse055d4d_email_from_name(v_display_name);

    IF v_email = '' THEN
        RETURN NULL;
    END IF;

    SELECT user_id
    INTO v_user_id
    FROM app_users
    WHERE lower(email) = lower(v_email)
       OR lower(display_name) = lower(v_display_name)
    ORDER BY is_active DESC, updated_at DESC
    LIMIT 1;

    -- Important:
    -- Do NOT insert missing @ussignal.com users here.
    -- Production trigger blocks manual cloud-domain users.
    -- Missing AE/SA/SAA are preserved in work_register_project_stakeholders
    -- by projectpulse055d4g_insert_snapshot_stakeholders().
    RETURN v_user_id;
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4g_email_from_name(display_name text)
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

    RETURN parts[1] || '.' || parts[array_length(parts, 1)] || '@ussignal.com';
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055[array_length(parts, 1)] || '@ussignal.com';
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4g_existing_user_for_name(display_name text)
RETURNS uuid
LANGUAGE plpgsql
AS $$
DECLARE
    v_display_name text;
    v_email text;
    v_user_id uuid;
BEGIN
    v_display_name := btrim(coalesce(display_name, ''));

    IF v_display_name = '' THEN
        RETURN NULL;
    END IF;

    v_email := projectpulse055d4g_email_from_name(v_display_name);

    SELECT user_id
    INTO v_user_id
    FROM app_users
    WHERE lower(email) = lower(v_email)
       OR lower(display_name) = lower(v_display_name)
    ORDER BY is_active DESC, updated_at DESC
    LIMIT 1;

    RETURN v_user_id;
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4g_add_snapshot_stakeholder(
    p_project_id uuid,
    p_role text,
    p_display_name text,
    p_actor_user_id uuid
)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    v_display_name text;
    v_email text;
    v_user_id uuid;
BEGIN
    v_display_name := btrim(coalesce(p_display_name, ''));

    IF v_display_name = '' THEN
        RETURN;
    END IF;

    v_email := projectpulse055d4g_email_from_name(v_display_name);
    v_user_id := projectpulse055d4g_existing_user_for_name(v_display_name);

    IF EXISTS (
        SELECT 1
        FROM work_register_project_stakeholders
        WHERE project_id = p_project_id
          AND lower(stakeholder_role) = lower(p_role)
          AND lower(display_name_snapshot) = lower(v_display_name)
    ) THEN
        RETURN;
    END IF;

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
        CASE WHEN v_user_id IS NULL THEN 'work_register_intake_email_snapshot_pending_entra_sync'
             ELSE 'work_register_intake_existing_user_link'
        END,
        p_actor_user_id,
        NOW()
    );
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4g_insert_snapshot_stakeholders()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_review jsonb;
BEGIN
    SELECT reviewed_json
    INTO v_review
    FROM work_register_intake_packages
    WHERE work_register_intake_package_id = NEW.work_register_intake_package_id;

    v_review := coalesce(v_review, '{}'::jsonb);

    PERFORM projectpulse055d4g_add_snapshot_stakeholder(
        NEW.project_id,
        'Account Executive',
        v_review->>'accountExecutiveName',
        NEW.committed_by_user_id
    );

    PERFORM projectpulse055d4g_add_snapshot_stakeholder(
        NEW.project_id,
        'Solution Architect',
        v_review->>'solutionArchitectName',
        NEW.committed_by_user_id
    );

    PERFORM projectpulse055d4g_add_snapshot_stakeholder(
        NEW.project_id,
        'Solution Architect Associate',
        v_review->>'insideSalesName',
        NEW.committed_by_user_id
    );

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_projectpulse055d4g_insert_snapshot_stakeholders
ON work_register_intake_commits;

CREATE TRIGGER trg_projectpulse055d4g_insert_snapshot_stakeholders
AFTER INSERT ON work_register_intake_commits
FOR EACH ROW
EXECUTE FUNCTION projectpulse055d4g_insert_snapshot_stakeholders();
