-- Roll back the Module 055C/055D date-persistence triggers and restore the
-- prior contract-code behavior. Existing human-readable contract data is
-- intentionally preserved because converting it back would be destructive.

BEGIN;

DROP TRIGGER IF EXISTS trg_projectpulse037_after_edit_save
    ON work_register_project_edit_save_audit;
DROP TRIGGER IF EXISTS trg_projectpulse037_after_intake_commit
    ON work_register_intake_commits;

DROP FUNCTION IF EXISTS projectpulse037_after_edit_save();
DROP FUNCTION IF EXISTS projectpulse037_after_intake_commit();
DROP FUNCTION IF EXISTS projectpulse037_apply_edit_fields(UUID, JSONB);
DROP FUNCTION IF EXISTS projectpulse037_payload_date(JSONB, TEXT[]);
DROP FUNCTION IF EXISTS projectpulse037_payload_text(JSONB, TEXT[]);

CREATE OR REPLACE FUNCTION projectpulse055d4m_normalize_contract(value TEXT)
RETURNS TEXT
LANGUAGE sql
AS $$
    SELECT CASE
        WHEN lower(coalesce(value, '')) IN ('fp','fixed price','fixedprice') THEN 'FP'
        WHEN lower(coalesce(value, '')) IN ('tm','t&m','time and material','time & material','timeandmaterial') THEN 'T&M'
        ELSE coalesce(nullif(value, ''), 'Not set')
    END;
$$;

CREATE OR REPLACE FUNCTION pp055d4t_contract(value TEXT)
RETURNS TEXT
LANGUAGE sql
AS $$
    SELECT CASE
        WHEN lower(coalesce(value, '')) IN ('fp', 'fixed price', 'fixedprice') THEN 'FP'
        WHEN lower(coalesce(value, '')) IN ('tm', 't&m', 'time and material', 'time & material', 'timeandmaterial') THEN 'T&M'
        ELSE coalesce(nullif(value, ''), '')
    END;
$$;

CREATE OR REPLACE FUNCTION pp055d4u_contract(value TEXT)
RETURNS TEXT
LANGUAGE sql
AS $$
    SELECT CASE
        WHEN lower(coalesce(value, '')) IN ('fp', 'fixed price', 'fixedprice') THEN 'FP'
        WHEN lower(coalesce(value, '')) IN ('tm', 't&m', 'time and material', 'time & material', 'timeandmaterial') THEN 'T&M'
        ELSE coalesce(nullif(value, ''), '')
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

DROP FUNCTION IF EXISTS projectpulse037_canonical_contract_type(TEXT);

DELETE FROM schema_migrations
WHERE migration_id = '037_work_register_dates_and_contract_types';

COMMIT;
