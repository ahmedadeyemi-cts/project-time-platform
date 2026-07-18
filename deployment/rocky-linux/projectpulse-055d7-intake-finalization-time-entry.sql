BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

ALTER TABLE projects
    ADD COLUMN IF NOT EXISTS work_type TEXT NOT NULL DEFAULT 'Project',
    ADD COLUMN IF NOT EXISTS contract_type TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS sell_quote_number TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS salesforce_id_number TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS certinia_id_number TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS sow_signed_date DATE NULL;

ALTER TABLE work_register_project_metadata
    ADD COLUMN IF NOT EXISTS requested_work_type TEXT NOT NULL DEFAULT 'Project',
    ADD COLUMN IF NOT EXISTS contract_type TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS sell_quote_number TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS salesforce_id_number TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS certinia_id_number TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS sow_signed_date DATE NULL,
    ADD COLUMN IF NOT EXISTS metadata_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW();

ALTER TABLE work_register_intake_packages
    ADD COLUMN IF NOT EXISTS sell_quote_number TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS salesforce_id_number TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS certinia_id_number TEXT NOT NULL DEFAULT '';

ALTER TABLE work_register_project_documents
    ADD COLUMN IF NOT EXISTS document_name TEXT NOT NULL DEFAULT '';

CREATE INDEX IF NOT EXISTS ix_projects_work_type_status
    ON projects (work_type, status);

CREATE INDEX IF NOT EXISTS ix_work_register_task_assignment_history_task_user
    ON work_register_task_assignment_history (project_id, task_id_text, assigned_user_id)
    WHERE assignment_status = 'active';

CREATE OR REPLACE FUNCTION projectpulse055d7_canonical_work_type(p_value TEXT)
RETURNS TEXT
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT CASE
        WHEN regexp_replace(lower(btrim(coalesce(p_value, ''))), '[^a-z0-9]+', '', 'g') = 'project'
            THEN 'Project'
        WHEN regexp_replace(lower(btrim(coalesce(p_value, ''))), '[^a-z0-9]+', '', 'g') = 'iqs'
            THEN 'IQS'
        WHEN regexp_replace(lower(btrim(coalesce(p_value, ''))), '[^a-z0-9]+', '', 'g') IN ('servicerequest', 'sr')
            THEN 'Service Request'
        WHEN regexp_replace(lower(btrim(coalesce(p_value, ''))), '[^a-z0-9]+', '', 'g') IN ('presales', 'presale')
            THEN 'Pre-sales'
        WHEN regexp_replace(lower(btrim(coalesce(p_value, ''))), '[^a-z0-9]+', '', 'g') IN ('internalproject', 'internal')
            THEN 'Internal Project'
        WHEN regexp_replace(lower(btrim(coalesce(p_value, ''))), '[^a-z0-9]+', '', 'g') = 'other'
            THEN 'Other'
        ELSE 'Other'
    END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d7_contract_type(p_value TEXT)
RETURNS TEXT
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT CASE
        WHEN regexp_replace(lower(btrim(coalesce(p_value, ''))), '[^a-z0-9]+', '', 'g') IN
             ('tm', 'timeandmaterial', 'timeandmaterials')
            THEN 'TM'
        WHEN regexp_replace(lower(btrim(coalesce(p_value, ''))), '[^a-z0-9]+', '', 'g') IN
             ('fp', 'fixedprice')
            THEN 'FP'
        ELSE btrim(coalesce(p_value, ''))
    END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d7_can_complete_intake(p_user_id UUID)
RETURNS BOOLEAN
LANGUAGE sql
STABLE
AS $$
    WITH allowed_terms AS (
        SELECT ARRAY[
            'super admin',
            'super administrator',
            'administrator',
            'admin',
            'project team coordinator',
            'project management',
            'project manager',
            'pmo',
            'project management team lead',
            'project management manager'
        ]::TEXT[] AS terms
    ),
    app_role_match AS (
        SELECT 1
          FROM app_user_role_assignments assignment
          JOIN app_roles role
            ON role.app_role_id = assignment.app_role_id
          CROSS JOIN allowed_terms allowed
         WHERE assignment.user_id = p_user_id
           AND assignment.is_active = TRUE
           AND role.is_active = TRUE
           AND EXISTS (
               SELECT 1
                 FROM unnest(allowed.terms) term
                WHERE lower(role.role_name) LIKE '%' || term || '%'
                   OR replace(lower(role.role_code), '_', ' ') LIKE '%' || term || '%'
           )
         LIMIT 1
    ),
    legacy_role_match AS (
        SELECT 1
          FROM user_roles assignment
          JOIN roles role
            ON role.role_id = assignment.role_id
          CROSS JOIN allowed_terms allowed
         WHERE assignment.user_id = p_user_id
           AND assignment.effective_start_date <= CURRENT_DATE
           AND (assignment.effective_end_date IS NULL OR assignment.effective_end_date >= CURRENT_DATE)
           AND EXISTS (
               SELECT 1
                 FROM unnest(allowed.terms) term
                WHERE lower(role.role_name) LIKE '%' || term || '%'
           )
         LIMIT 1
    ),
    team_match AS (
        SELECT 1
          FROM team_memberships membership
          JOIN teams team
            ON team.team_id = membership.team_id
          CROSS JOIN allowed_terms allowed
         WHERE membership.user_id = p_user_id
           AND team.is_active = TRUE
           AND membership.effective_start_date <= CURRENT_DATE
           AND (membership.effective_end_date IS NULL OR membership.effective_end_date >= CURRENT_DATE)
           AND EXISTS (
               SELECT 1
                 FROM unnest(allowed.terms) term
                WHERE lower(team.team_name) LIKE '%' || term || '%'
           )
         LIMIT 1
    ),
    profile_match AS (
        SELECT 1
          FROM app_users app_user
          CROSS JOIN allowed_terms allowed
         WHERE app_user.user_id = p_user_id
           AND app_user.is_active = TRUE
           AND EXISTS (
               SELECT 1
                 FROM unnest(allowed.terms) term
                WHERE lower(coalesce(app_user.job_title, '')) LIKE '%' || term || '%'
                   OR lower(coalesce(app_user.department, '')) LIKE '%' || term || '%'
                   OR lower(coalesce(app_user.department_name, '')) LIKE '%' || term || '%'
                   OR lower(coalesce(app_user.team_name, '')) LIKE '%' || term || '%'
           )
         LIMIT 1
    )
    SELECT EXISTS (
        SELECT 1 FROM app_role_match
        UNION ALL SELECT 1 FROM legacy_role_match
        UNION ALL SELECT 1 FROM team_match
        UNION ALL SELECT 1 FROM profile_match
    );
$$;

CREATE OR REPLACE FUNCTION projectpulse055d7_finalize_intake_commit(
    p_intake_package_id UUID,
    p_project_id UUID,
    p_actor_user_id UUID
)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
    v_package RECORD;
    v_review JSONB;
    v_project_name TEXT;
    v_work_type TEXT;
    v_contract_type TEXT;
    v_sell_quote TEXT;
    v_salesforce_id TEXT;
    v_certinia_id TEXT;
    v_sow_signed_date DATE;
    v_documents_named INTEGER := 0;
    v_assignments_updated INTEGER := 0;
    v_assignments_inserted INTEGER := 0;
BEGIN
    SELECT *
      INTO v_package
      FROM work_register_intake_packages
     WHERE work_register_intake_package_id = p_intake_package_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Intake package % was not found during finalization.', p_intake_package_id;
    END IF;

    SELECT project_name
      INTO v_project_name
      FROM projects
     WHERE project_id = p_project_id
     FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Project % was not found during intake finalization.', p_project_id;
    END IF;

    v_review := coalesce(v_package.reviewed_json, '{}'::jsonb);

    v_work_type := projectpulse055d7_canonical_work_type(
        coalesce(
            nullif(v_review->>'requestedWorkType', ''),
            nullif(v_package.requested_work_type, ''),
            'Project'
        )
    );

    v_contract_type := projectpulse055d7_contract_type(
        coalesce(
            nullif(v_review->>'contractType', ''),
            nullif(v_package.contract_type, ''),
            ''
        )
    );

    v_sell_quote := btrim(coalesce(
        nullif(v_review->>'sellQuoteNumber', ''),
        nullif(v_package.sell_quote_number, ''),
        ''
    ));

    v_salesforce_id := btrim(coalesce(
        nullif(v_review->>'salesforceIdNumber', ''),
        nullif(v_package.salesforce_id_number, ''),
        ''
    ));

    v_certinia_id := btrim(coalesce(
        nullif(v_review->>'certiniaIdNumber', ''),
        nullif(v_package.certinia_id_number, ''),
        ''
    ));

    BEGIN
        v_sow_signed_date := nullif(v_review->>'sowSignedDate', '')::date;
    EXCEPTION WHEN OTHERS THEN
        RAISE EXCEPTION 'Invalid SOW signed date for intake package %.', p_intake_package_id;
    END;

    UPDATE projects
       SET work_type = v_work_type,
           contract_type = coalesce(nullif(v_contract_type, ''), contract_type),
           sell_quote_number = coalesce(nullif(v_sell_quote, ''), sell_quote_number),
           salesforce_id_number = coalesce(nullif(v_salesforce_id, ''), salesforce_id_number),
           certinia_id_number = coalesce(nullif(v_certinia_id, ''), certinia_id_number),
           sow_signed_date = coalesce(v_sow_signed_date, sow_signed_date),
           updated_at = NOW()
     WHERE project_id = p_project_id;

    UPDATE work_register_project_metadata
       SET requested_work_type = v_work_type,
           contract_type = coalesce(nullif(v_contract_type, ''), contract_type),
           sell_quote_number = coalesce(nullif(v_sell_quote, ''), sell_quote_number),
           salesforce_id_number = coalesce(nullif(v_salesforce_id, ''), salesforce_id_number),
           certinia_id_number = coalesce(nullif(v_certinia_id, ''), certinia_id_number),
           sow_signed_date = coalesce(v_sow_signed_date, sow_signed_date),
           metadata_json = coalesce(metadata_json, '{}'::jsonb) || jsonb_build_object(
               'requestedWorkType', v_work_type,
               'contractType', v_contract_type,
               'sellQuoteNumber', v_sell_quote,
               'salesforceIdNumber', v_salesforce_id,
               'certiniaIdNumber', v_certinia_id,
               'sowSignedDate', v_sow_signed_date
           ),
           updated_at = NOW()
     WHERE project_id = p_project_id;

    UPDATE work_register_project_documents
       SET document_name = CASE upper(btrim(document_type))
           WHEN 'GSD' THEN 'GSD_' || v_project_name
           WHEN 'SOW' THEN 'SOW_' || v_project_name
           ELSE coalesce(nullif(document_name, ''), nullif(original_file_name, ''), 'Document_' || v_project_name)
       END
     WHERE project_id = p_project_id;

    GET DIAGNOSTICS v_documents_named = ROW_COUNT;

    WITH latest_assignments AS (
        SELECT DISTINCT ON (h.project_id, h.task_id_text, h.assigned_user_id)
               h.project_id,
               CASE WHEN h.task_id_text ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' THEN h.task_id_text::uuid END AS task_id,
               h.assigned_user_id,
               h.changed_by_user_id,
               h.effective_start_date,
               h.effective_end_date,
               h.allocation_percent,
               h.allocated_hours,
               h.change_reason,
               h.created_at
          FROM work_register_task_assignment_history h
          JOIN project_tasks task
            ON task.project_id = h.project_id
           AND task.task_id = CASE WHEN h.task_id_text ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' THEN h.task_id_text::uuid END
         WHERE h.project_id = p_project_id
           AND h.assigned_user_id IS NOT NULL
           AND h.assignment_status = 'active'
           AND (h.effective_end_date IS NULL OR h.effective_end_date >= CURRENT_DATE)
           AND h.task_id_text ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
         ORDER BY h.project_id, h.task_id_text, h.assigned_user_id, h.created_at DESC
    )
    UPDATE project_assignments assignment
       SET assigned_by_user_id = coalesce(source.changed_by_user_id, p_actor_user_id),
           effective_start_date = source.effective_start_date,
           effective_end_date = source.effective_end_date,
           allocation_percent = nullif(source.allocation_percent, 0),
           assigned_hours = coalesce(source.allocated_hours, 0),
           assignment_source = 'work_register_intake_final_save',
           assignment_notes = coalesce(nullif(source.change_reason, ''), 'Synchronized from Work Register intake final save.'),
           updated_at = NOW()
      FROM latest_assignments source
     WHERE assignment.project_id = source.project_id
       AND assignment.task_id = source.task_id
       AND assignment.user_id = source.assigned_user_id;

    GET DIAGNOSTICS v_assignments_updated = ROW_COUNT;

    WITH latest_assignments AS (
        SELECT DISTINCT ON (h.project_id, h.task_id_text, h.assigned_user_id)
               h.project_id,
               CASE WHEN h.task_id_text ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' THEN h.task_id_text::uuid END AS task_id,
               h.assigned_user_id,
               h.changed_by_user_id,
               h.effective_start_date,
               h.effective_end_date,
               h.allocation_percent,
               h.allocated_hours,
               h.change_reason,
               h.created_at
          FROM work_register_task_assignment_history h
          JOIN project_tasks task
            ON task.project_id = h.project_id
           AND task.task_id = CASE WHEN h.task_id_text ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' THEN h.task_id_text::uuid END
         WHERE h.project_id = p_project_id
           AND h.assigned_user_id IS NOT NULL
           AND h.assignment_status = 'active'
           AND (h.effective_end_date IS NULL OR h.effective_end_date >= CURRENT_DATE)
           AND h.task_id_text ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
         ORDER BY h.project_id, h.task_id_text, h.assigned_user_id, h.created_at DESC
    )
    INSERT INTO project_assignments (
        project_id,
        task_id,
        user_id,
        assigned_by_user_id,
        effective_start_date,
        effective_end_date,
        allocation_percent,
        assigned_hours,
        assignment_source,
        assignment_notes,
        updated_at
    )
    SELECT source.project_id,
           source.task_id,
           source.assigned_user_id,
           coalesce(source.changed_by_user_id, p_actor_user_id),
           source.effective_start_date,
           source.effective_end_date,
           nullif(source.allocation_percent, 0),
           coalesce(source.allocated_hours, 0),
           'work_register_intake_final_save',
           coalesce(nullif(source.change_reason, ''), 'Synchronized from Work Register intake final save.'),
           NOW()
      FROM latest_assignments source
     WHERE NOT EXISTS (
         SELECT 1
           FROM project_assignments existing
          WHERE existing.project_id = source.project_id
            AND existing.task_id = source.task_id
            AND existing.user_id = source.assigned_user_id
     );

    GET DIAGNOSTICS v_assignments_inserted = ROW_COUNT;

    RETURN jsonb_build_object(
        'status', 'ok',
        'projectId', p_project_id,
        'requestedWorkType', v_work_type,
        'contractType', v_contract_type,
        'documentsNamed', v_documents_named,
        'assignmentsUpdated', v_assignments_updated,
        'assignmentsInserted', v_assignments_inserted
    );
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse055d7_sync_task_assignment_history()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    v_task_id UUID;
    v_rows_updated INTEGER := 0;
BEGIN
    IF NEW.task_id_text !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' THEN
        RETURN NEW;
    END IF;

    v_task_id := NEW.task_id_text::uuid;

    IF NOT EXISTS (
        SELECT 1
          FROM project_tasks task
         WHERE task.task_id = v_task_id
           AND task.project_id = NEW.project_id
    ) THEN
        RETURN NEW;
    END IF;

    IF TG_OP = 'UPDATE'
       AND OLD.assigned_user_id IS NOT NULL
       AND (
           OLD.assigned_user_id IS DISTINCT FROM NEW.assigned_user_id
           OR NEW.assignment_status <> 'active'
           OR (NEW.effective_end_date IS NOT NULL AND NEW.effective_end_date < CURRENT_DATE)
       ) THEN
        UPDATE project_assignments
           SET effective_end_date = coalesce(NEW.effective_end_date, CURRENT_DATE - 1),
               assignment_source = 'work_register_assignment_history',
               assignment_notes = coalesce(nullif(NEW.change_reason, ''), 'Assignment closed from Work Register task roster.'),
               updated_at = NOW()
         WHERE project_id = NEW.project_id
           AND task_id = v_task_id
           AND user_id = OLD.assigned_user_id;
    END IF;

    IF NEW.assigned_user_id IS NULL THEN
        RETURN NEW;
    END IF;

    IF NEW.assignment_status = 'active'
       AND (NEW.effective_end_date IS NULL OR NEW.effective_end_date >= CURRENT_DATE) THEN
        UPDATE project_assignments
           SET assigned_by_user_id = coalesce(NEW.changed_by_user_id, assigned_by_user_id),
               effective_start_date = NEW.effective_start_date,
               effective_end_date = NEW.effective_end_date,
               allocation_percent = nullif(NEW.allocation_percent, 0),
               assigned_hours = coalesce(NEW.allocated_hours, 0),
               assignment_source = 'work_register_assignment_history',
               assignment_notes = coalesce(nullif(NEW.change_reason, ''), 'Synchronized from Work Register task roster.'),
               updated_at = NOW()
         WHERE project_id = NEW.project_id
           AND task_id = v_task_id
           AND user_id = NEW.assigned_user_id;

        GET DIAGNOSTICS v_rows_updated = ROW_COUNT;

        IF v_rows_updated = 0 THEN
            INSERT INTO project_assignments (
                project_id,
                task_id,
                user_id,
                assigned_by_user_id,
                effective_start_date,
                effective_end_date,
                allocation_percent,
                assigned_hours,
                assignment_source,
                assignment_notes,
                updated_at
            )
            VALUES (
                NEW.project_id,
                v_task_id,
                NEW.assigned_user_id,
                NEW.changed_by_user_id,
                NEW.effective_start_date,
                NEW.effective_end_date,
                nullif(NEW.allocation_percent, 0),
                coalesce(NEW.allocated_hours, 0),
                'work_register_assignment_history',
                coalesce(nullif(NEW.change_reason, ''), 'Synchronized from Work Register task roster.'),
                NOW()
            );
        END IF;
    ELSE
        UPDATE project_assignments
           SET effective_end_date = coalesce(NEW.effective_end_date, CURRENT_DATE - 1),
               assignment_source = 'work_register_assignment_history',
               assignment_notes = coalesce(nullif(NEW.change_reason, ''), 'Assignment closed from Work Register task roster.'),
               updated_at = NOW()
         WHERE project_id = NEW.project_id
           AND task_id = v_task_id
           AND user_id = NEW.assigned_user_id;
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_projectpulse055d7_sync_task_assignment_history
    ON work_register_task_assignment_history;

CREATE TRIGGER trg_projectpulse055d7_sync_task_assignment_history
AFTER INSERT OR UPDATE OF
    assigned_user_id,
    assignment_status,
    effective_start_date,
    effective_end_date,
    allocated_hours,
    allocation_percent
ON work_register_task_assignment_history
FOR EACH ROW
EXECUTE FUNCTION projectpulse055d7_sync_task_assignment_history();

CREATE OR REPLACE FUNCTION projectpulse055d7_after_intake_commit()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    v_result JSONB;
BEGIN
    v_result := projectpulse055d7_finalize_intake_commit(
        NEW.work_register_intake_package_id,
        NEW.project_id,
        NEW.committed_by_user_id
    );

    IF coalesce(v_result->>'status', '') <> 'ok' THEN
        RAISE EXCEPTION 'Intake finalization failed: %', v_result::text;
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_projectpulse055d7_after_intake_commit
    ON work_register_intake_commits;

CREATE TRIGGER trg_projectpulse055d7_after_intake_commit
AFTER INSERT ON work_register_intake_commits
FOR EACH ROW
EXECUTE FUNCTION projectpulse055d7_after_intake_commit();

-- Repair existing committed intakes, including the first post-Azure-Files test.
DO $$
DECLARE
    v_commit RECORD;
BEGIN
    FOR v_commit IN
        SELECT work_register_intake_package_id,
               project_id,
               committed_by_user_id
          FROM work_register_intake_commits
         ORDER BY committed_at NULLS LAST, work_register_intake_package_id
    LOOP
        BEGIN
            PERFORM projectpulse055d7_finalize_intake_commit(
                v_commit.work_register_intake_package_id,
                v_commit.project_id,
                v_commit.committed_by_user_id
            );
        EXCEPTION WHEN OTHERS THEN
            RAISE WARNING '055D.7 backfill skipped package % / project %: %',
                v_commit.work_register_intake_package_id,
                v_commit.project_id,
                SQLERRM;
        END;
    END LOOP;
END;
$$;

COMMIT;
