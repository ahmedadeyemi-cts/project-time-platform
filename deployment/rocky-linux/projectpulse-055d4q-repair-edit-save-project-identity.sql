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

CREATE OR REPLACE FUNCTION projectpulse055d4q_payload_value(payload jsonb, VARIADIC keys text[])
RETURNS text
LANGUAGE plpgsql
AS $$
DECLARE
    key text;
    wrapper text;
    value text;
BEGIN
    IF payload IS NULL THEN
        RETURN NULL;
    END IF;

    FOREACH key IN ARRAY keys LOOP
        value := payload->>key;
        IF value IS NOT NULL AND btrim(value) <> '' THEN
            RETURN value;
        END IF;
    END LOOP;

    FOREACH wrapper IN ARRAY ARRAY[
        'project',
        'work',
        'item',
        'row',
        'record',
        'selectedProject',
        'selectedWork',
        'selectedWorkRegisterProject',
        'editProject',
        'editingProject',
        'editForm',
        'form',
        'values',
        'payload',
        'projectUpdate',
        'setup',
        'setupForm'
    ] LOOP
        IF jsonb_typeof(payload->wrapper) = 'object' THEN
            FOREACH key IN ARRAY keys LOOP
                value := payload #>> ARRAY[wrapper, key];
                IF value IS NOT NULL AND btrim(value) <> '' THEN
                    RETURN value;
                END IF;
            END LOOP;
        END IF;
    END LOOP;

    RETURN NULL;
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4q_resolve_project_id(payload jsonb)
RETURNS uuid
LANGUAGE plpgsql
AS $$
DECLARE
    v_id_text text;
    v_project_id uuid;
    v_project_code text;
    v_project_name text;
BEGIN
    v_id_text := projectpulse055d4q_payload_value(
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

    v_project_id := projectpulse055d4m_uuid_or_null(v_id_text);

    IF v_project_id IS NOT NULL THEN
        RETURN v_project_id;
    END IF;

    v_project_code := projectpulse055d4q_payload_value(
        payload,
        'projectCode',
        'project_code',
        'workCode',
        'work_code',
        'code'
    );

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

    v_project_name := projectpulse055d4q_payload_value(
        payload,
        'projectName',
        'project_name',
        'name'
    );

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
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4m_json_text(payload jsonb, VARIADIC keys text[])
RETURNS text
LANGUAGE plpgsql
AS $$
DECLARE
    value text;
    resolved_id uuid;
BEGIN
    value := projectpulse055d4q_payload_value(payload, VARIADIC keys);

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
        resolved_id := projectpulse055d4q_resolve_project_id(payload);

        IF resolved_id IS NOT NULL THEN
            RETURN resolved_id::text;
        END IF;
    END IF;

    RETURN NULL;
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d4q_project_edit_payload_debug(payload jsonb)
RETURNS jsonb
LANGUAGE plpgsql
AS $$
DECLARE
    resolved_id uuid;
BEGIN
    resolved_id := projectpulse055d4q_resolve_project_id(payload);

    RETURN jsonb_build_object(
        'resolvedProjectId', resolved_id,
        'topLevelKeys', COALESCE((SELECT jsonb_agg(key) FROM jsonb_object_keys(payload) key), '[]'::jsonb),
        'projectIdText', projectpulse055d4q_payload_value(payload, 'projectId', 'project_id', 'id', 'workId', 'work_id'),
        'projectCode', projectpulse055d4q_payload_value(payload, 'projectCode', 'project_code', 'workCode', 'work_code', 'code'),
        'projectName', projectpulse055d4q_payload_value(payload, 'projectName', 'project_name', 'name')
    );
END;
$$;
