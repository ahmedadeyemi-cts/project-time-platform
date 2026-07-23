-- ProjectPulse Modules 055C and 055D date persistence and contract normalization
-- Keeps the GSD source codes T&M and FP compatible while storing and presenting
-- one canonical value for each billing model.

BEGIN;

CREATE OR REPLACE FUNCTION projectpulse037_canonical_contract_type(p_value TEXT)
RETURNS TEXT
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT CASE
        WHEN regexp_replace(lower(btrim(coalesce(p_value, ''))), '[^a-z0-9]+', '', 'g') IN
             ('tm', 'timeandmaterial', 'timeandmaterials')
            THEN 'Time and Material'
        WHEN regexp_replace(lower(btrim(coalesce(p_value, ''))), '[^a-z0-9]+', '', 'g') IN
             ('fp', 'fixedprice')
            THEN 'Fixed Price'
        WHEN regexp_replace(lower(btrim(coalesce(p_value, ''))), '[^a-z0-9]+', '', 'g') IN
             ('presales', 'presale')
            THEN 'Pre-Sales'
        WHEN regexp_replace(lower(btrim(coalesce(p_value, ''))), '[^a-z0-9]+', '', 'g') = 'internal'
            THEN 'Internal'
        WHEN regexp_replace(lower(btrim(coalesce(p_value, ''))), '[^a-z0-9]+', '', 'g') = 'nonbillable'
            THEN 'Non-billable'
        ELSE btrim(coalesce(p_value, ''))
    END;
$$;

-- Keep every existing Work Register write path on the same canonical mapping.
CREATE OR REPLACE FUNCTION projectpulse055d4m_normalize_contract(value TEXT)
RETURNS TEXT
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT coalesce(nullif(projectpulse037_canonical_contract_type(value), ''), 'Not set');
$$;

CREATE OR REPLACE FUNCTION pp055d4t_contract(value TEXT)
RETURNS TEXT
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT projectpulse037_canonical_contract_type(value);
$$;

CREATE OR REPLACE FUNCTION pp055d4u_contract(value TEXT)
RETURNS TEXT
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT projectpulse037_canonical_contract_type(value);
$$;

CREATE OR REPLACE FUNCTION projectpulse055d7_contract_type(p_value TEXT)
RETURNS TEXT
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT projectpulse037_canonical_contract_type(p_value);
$$;

CREATE OR REPLACE FUNCTION projectpulse037_payload_text(
    p_payload JSONB,
    VARIADIC p_keys TEXT[]
)
RETURNS TEXT
LANGUAGE plpgsql
IMMUTABLE
AS $$
DECLARE
    v_key TEXT;
    v_value TEXT;
BEGIN
    FOREACH v_key IN ARRAY p_keys LOOP
        IF coalesce(p_payload, '{}'::jsonb) ? v_key THEN
            v_value := btrim(coalesce(p_payload->>v_key, ''));
            IF v_value <> '' THEN
                RETURN v_value;
            END IF;
        END IF;
    END LOOP;

    RETURN NULL;
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse037_payload_date(
    p_payload JSONB,
    VARIADIC p_keys TEXT[]
)
RETURNS DATE
LANGUAGE plpgsql
IMMUTABLE
AS $$
DECLARE
    v_value TEXT;
BEGIN
    v_value := projectpulse037_payload_text(p_payload, VARIADIC p_keys);
    IF v_value IS NULL THEN
        RETURN NULL;
    END IF;

    RETURN v_value::date;
EXCEPTION WHEN OTHERS THEN
    RETURN NULL;
END;
$$;

CREATE OR REPLACE FUNCTION projectpulse037_apply_edit_fields(
    p_project_id UUID,
    p_payload JSONB
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
DECLARE
    v_contract_source TEXT;
    v_contract_type TEXT;
    v_project_start_source TEXT;
    v_estimated_end_source TEXT;
    v_project_start_date DATE;
    v_estimated_end_date DATE;
    v_existing_start_date DATE;
    v_existing_end_date DATE;
    v_sow_signed_date DATE;
    v_metadata_payload JSONB := coalesce(p_payload, '{}'::jsonb);
BEGIN
    v_contract_source := projectpulse037_payload_text(
        p_payload,
        'contractType',
        'contract_type',
        'contract'
    );
    v_contract_type := projectpulse037_canonical_contract_type(v_contract_source);
    v_project_start_source := projectpulse037_payload_text(
        p_payload,
        'projectStartDate',
        'startDate',
        'start_date',
        'plannedStartDate'
    );
    v_project_start_date := projectpulse037_payload_date(
        p_payload,
        'projectStartDate',
        'startDate',
        'start_date',
        'plannedStartDate'
    );
    v_estimated_end_source := projectpulse037_payload_text(
        p_payload,
        'estimatedEndDate',
        'endDate',
        'end_date',
        'projectEndDate',
        'plannedEndDate'
    );
    v_estimated_end_date := projectpulse037_payload_date(
        p_payload,
        'estimatedEndDate',
        'endDate',
        'end_date',
        'projectEndDate',
        'plannedEndDate'
    );
    v_sow_signed_date := projectpulse037_payload_date(
        p_payload,
        'sowSignedDate',
        'sow_signed_date',
        'sowDate',
        'sow_date'
    );

    SELECT start_date,
           end_date
      INTO v_existing_start_date,
           v_existing_end_date
      FROM projects
     WHERE project_id = p_project_id;

    IF v_project_start_source IS NOT NULL AND v_project_start_date IS NULL THEN
        v_metadata_payload := v_metadata_payload - ARRAY[
            'projectStartDate', 'startDate', 'start_date', 'plannedStartDate'
        ];
    END IF;

    IF v_estimated_end_source IS NOT NULL AND v_estimated_end_date IS NULL THEN
        v_metadata_payload := v_metadata_payload - ARRAY[
            'estimatedEndDate', 'endDate', 'end_date',
            'projectEndDate', 'plannedEndDate'
        ];
    END IF;

    IF coalesce(v_estimated_end_date, v_existing_end_date) IS NOT NULL
       AND coalesce(v_project_start_date, v_existing_start_date) IS NOT NULL
       AND coalesce(v_estimated_end_date, v_existing_end_date)
           < coalesce(v_project_start_date, v_existing_start_date)
    THEN
        -- Invalid historical or partial date edits must not abort migration
        -- replay or a future non-UI save. Ignore only the supplied date keys.
        IF v_project_start_source IS NOT NULL THEN
            v_project_start_date := NULL;
            v_metadata_payload := v_metadata_payload - ARRAY[
                'projectStartDate', 'startDate', 'start_date', 'plannedStartDate'
            ];
        END IF;
        IF v_estimated_end_source IS NOT NULL THEN
            v_estimated_end_date := NULL;
            v_metadata_payload := v_metadata_payload - ARRAY[
                'estimatedEndDate', 'endDate', 'end_date',
                'projectEndDate', 'plannedEndDate'
            ];
        END IF;
    END IF;

    IF v_contract_source IS NULL
       AND v_project_start_date IS NULL
       AND v_estimated_end_date IS NULL
       AND v_sow_signed_date IS NULL
    THEN
        RETURN;
    END IF;

    UPDATE projects
       SET contract_type = CASE
               WHEN v_contract_source IS NOT NULL THEN v_contract_type
               ELSE contract_type
           END,
           start_date = coalesce(v_project_start_date, start_date),
           end_date = coalesce(v_estimated_end_date, end_date),
           sow_signed_date = coalesce(v_sow_signed_date, sow_signed_date),
           updated_at = NOW()
     WHERE project_id = p_project_id;

    UPDATE work_register_project_metadata
       SET contract_type = CASE
               WHEN v_contract_source IS NOT NULL THEN v_contract_type
               ELSE contract_type
           END,
           sow_signed_date = coalesce(v_sow_signed_date, sow_signed_date),
           metadata_json = coalesce(metadata_json, '{}'::jsonb)
               || v_metadata_payload,
           updated_at = NOW()
     WHERE project_id = p_project_id;

    RETURN;
END;
$$;

-- The current edit-save function records the full request in this durable audit
-- table. Persist the date aliases from that exact request after the audit row is
-- inserted so 055C cannot lose estimatedEndDate or projectStartDate again.
CREATE OR REPLACE FUNCTION projectpulse037_after_edit_save()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM projectpulse037_apply_edit_fields(NEW.project_id, NEW.payload_json);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_projectpulse037_after_edit_save
    ON work_register_project_edit_save_audit;

CREATE TRIGGER trg_projectpulse037_after_edit_save
AFTER INSERT ON work_register_project_edit_save_audit
FOR EACH ROW
EXECUTE FUNCTION projectpulse037_after_edit_save();

-- Replay preserved edit evidence in chronological order. This repairs date
-- values that users already attempted to save before migration 037.
DO $$
DECLARE
    v_audit RECORD;
BEGIN
    FOR v_audit IN
        SELECT project_id,
               payload_json
          FROM work_register_project_edit_save_audit
         WHERE project_id IS NOT NULL
           AND payload_json ?| ARRAY[
               'contractType',
               'contract_type',
               'projectStartDate',
               'startDate',
               'estimatedEndDate',
               'endDate',
               'sowSignedDate',
               'sow_signed_date'
           ]
         ORDER BY created_at,
                  work_register_project_edit_save_audit_id
    LOOP
        PERFORM projectpulse037_apply_edit_fields(
            v_audit.project_id,
            v_audit.payload_json
        );
    END LOOP;
END;
$$;

-- 055D final creation commits from reviewed_json. Carry both dates and the
-- canonical contract type from the reviewed package into the created project.
CREATE OR REPLACE FUNCTION projectpulse037_after_intake_commit()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    v_review JSONB;
    v_package_contract_type TEXT;
    v_contract_type TEXT;
    v_project_start_date DATE;
    v_estimated_end_date DATE;
    v_sow_signed_date DATE;
BEGIN
    SELECT coalesce(reviewed_json, '{}'::jsonb),
           coalesce(contract_type, '')
      INTO v_review,
           v_package_contract_type
      FROM work_register_intake_packages
     WHERE work_register_intake_package_id = NEW.work_register_intake_package_id;

    v_contract_type := projectpulse037_canonical_contract_type(
        coalesce(
            projectpulse037_payload_text(v_review, 'contractType', 'contract_type'),
            v_package_contract_type
        )
    );
    v_estimated_end_date := projectpulse037_payload_date(
        v_review,
        'estimatedEndDate',
        'endDate',
        'end_date',
        'projectEndDate',
        'plannedEndDate'
    );
    v_sow_signed_date := projectpulse037_payload_date(
        v_review,
        'sowSignedDate',
        'sow_signed_date',
        'sowDate',
        'sow_date'
    );

    SELECT start_date
      INTO v_project_start_date
      FROM projects
     WHERE project_id = NEW.project_id;

    IF v_estimated_end_date IS NOT NULL
       AND v_project_start_date IS NOT NULL
       AND v_estimated_end_date < v_project_start_date
    THEN
        -- The API returns a validation message before commit. This guard keeps
        -- direct or legacy callers from violating chk_project_dates.
        v_estimated_end_date := NULL;
    END IF;

    UPDATE projects
       SET contract_type = coalesce(nullif(v_contract_type, ''), contract_type),
           end_date = coalesce(v_estimated_end_date, end_date),
           sow_signed_date = coalesce(v_sow_signed_date, sow_signed_date),
           updated_at = NOW()
     WHERE project_id = NEW.project_id;

    UPDATE work_register_project_metadata
       SET contract_type = coalesce(nullif(v_contract_type, ''), contract_type),
           sow_signed_date = coalesce(v_sow_signed_date, sow_signed_date),
           metadata_json = coalesce(metadata_json, '{}'::jsonb)
               || jsonb_strip_nulls(jsonb_build_object(
                   'contractType', nullif(v_contract_type, ''),
                   'sowSignedDate', v_sow_signed_date,
                   'estimatedEndDate', v_estimated_end_date
               )),
           updated_at = NOW()
     WHERE project_id = NEW.project_id;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_projectpulse037_after_intake_commit
    ON work_register_intake_commits;

CREATE TRIGGER trg_projectpulse037_after_intake_commit
AFTER INSERT ON work_register_intake_commits
FOR EACH ROW
EXECUTE FUNCTION projectpulse037_after_intake_commit();

-- Consolidate existing recognized variants without changing unrelated contract
-- classifications.
UPDATE projects
   SET contract_type = projectpulse037_canonical_contract_type(contract_type),
       updated_at = NOW()
 WHERE regexp_replace(lower(btrim(coalesce(contract_type, ''))), '[^a-z0-9]+', '', 'g') IN
       ('tm', 'timeandmaterial', 'timeandmaterials', 'fp', 'fixedprice');

UPDATE work_register_project_metadata
   SET contract_type = projectpulse037_canonical_contract_type(contract_type),
       updated_at = NOW()
 WHERE regexp_replace(lower(btrim(coalesce(contract_type, ''))), '[^a-z0-9]+', '', 'g') IN
       ('tm', 'timeandmaterial', 'timeandmaterials', 'fp', 'fixedprice');

UPDATE work_register_intake_packages
   SET contract_type = projectpulse037_canonical_contract_type(contract_type),
       updated_at = NOW()
 WHERE regexp_replace(lower(btrim(coalesce(contract_type, ''))), '[^a-z0-9]+', '', 'g') IN
       ('tm', 'timeandmaterial', 'timeandmaterials', 'fp', 'fixedprice');

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '037_work_register_dates_and_contract_types',
    'Persist 055C/055D SOW and estimated-end dates and map GSD T&M/FP codes to canonical contract labels',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
