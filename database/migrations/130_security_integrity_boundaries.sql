-- Enforce data integrity at the database boundary shared by legacy and current APIs.
BEGIN;
SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='60s';

ALTER TABLE app_users ADD COLUMN IF NOT EXISTS profile_photo_data_url TEXT NOT NULL DEFAULT '';
ALTER TABLE app_users ADD COLUMN IF NOT EXISTS profile_photo_updated_at TIMESTAMPTZ;
ALTER TABLE email_notification_outbox ADD COLUMN IF NOT EXISTS queued_by_user_id UUID REFERENCES app_users(user_id);

CREATE TABLE IF NOT EXISTS security_integrity_repair_events (
    repair_key TEXT NOT NULL,
    entity_id UUID NOT NULL,
    previous_value JSONB NOT NULL,
    repaired_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY(repair_key,entity_id)
);

-- Classification is metadata, never an authorization grant.
CREATE OR REPLACE FUNCTION projectpulse086_classify_flowhive_document()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE candidate TEXT;
BEGIN
    candidate := lower(COALESCE(NEW.original_file_name,'') || ' ' || COALESCE(NEW.document_type,'') || ' ' || COALESCE(NEW.document_category,''));
    IF lower(COALESCE(NEW.document_category,'')) IN ('','other','supporting','project_document') THEN
        IF candidate ~ '(^|[^a-z])(statement[ _-]*of[ _-]*work|sow)([^a-z]|$)' THEN NEW.document_category := 'sow';
        ELSIF candidate ~ '(^|[^a-z])(global[ _-]*solution[ _-]*design|gsd)([^a-z]|$)' THEN NEW.document_category := 'gsd';
        END IF;
    END IF;
    RETURN NEW;
END; $$;
WITH changed AS (
    INSERT INTO security_integrity_repair_events(repair_key,entity_id,previous_value)
    SELECT 'document_visibility',d.project_intake_document_id,jsonb_build_object('engineeringVisible',d.engineering_visible)
    FROM project_intake_documents d
    JOIN work_register_documents w ON w.work_register_document_id=d.work_register_document_id
    WHERE d.engineering_visible AND lower(COALESCE(w.visibility,'')) NOT IN ('project_team','engineering_team','all')
    ON CONFLICT DO NOTHING RETURNING entity_id
)
UPDATE project_intake_documents d SET engineering_visible=FALSE
FROM work_register_documents w
WHERE d.work_register_document_id=w.work_register_document_id
  AND d.engineering_visible
  AND lower(COALESCE(w.visibility,'')) NOT IN ('project_team','engineering_team','all');

DELETE FROM app_role_permissions rp USING app_roles r,app_permissions p
WHERE rp.app_role_id=r.app_role_id AND rp.app_permission_id=p.app_permission_id
 AND upper(r.role_code) IN ('PROJECT_MANAGER','PROJECT_MANAGEMENT','PROJECT_MANAGEMENT_LEAD','PROJECT_MANAGEMENT_TEAM_LEAD','PM_TEAM_LEAD')
 AND p.permission_code IN ('VIEW_PROJECT_ALLOCATION_INFO','MANAGE_PROJECT_ALLOCATION_INFO');

CREATE OR REPLACE FUNCTION projectpulse073_add_working_days(source_date DATE,working_days INTEGER)
RETURNS DATE LANGUAGE plpgsql STABLE AS $$
DECLARE result_date DATE:=source_date; remaining INTEGER; direction INTEGER; iterations INTEGER:=0;
BEGIN
 IF source_date IS NULL THEN RETURN NULL; END IF;
 IF source_date NOT BETWEEN DATE '2000-01-01' AND DATE '2100-12-31' OR COALESCE(working_days,0) NOT BETWEEN -730 AND 730 THEN
  RAISE EXCEPTION 'Schedule is outside the supported working-day horizon.' USING ERRCODE='22023';
 END IF;
 remaining:=abs(COALESCE(working_days,0)); direction:=CASE WHEN working_days<0 THEN -1 ELSE 1 END;
 WHILE remaining>0 LOOP
  iterations:=iterations+1;
  IF iterations>1096 THEN RAISE EXCEPTION 'Working-day iteration limit exceeded.' USING ERRCODE='22023'; END IF;
  result_date:=result_date+direction;
  IF projectpulse073_is_working_day(result_date) THEN remaining:=remaining-1; END IF;
 END LOOP;
 RETURN result_date;
END; $$;
CREATE OR REPLACE FUNCTION projectpulse073_working_day_delta(old_date DATE,new_date DATE)
RETURNS INTEGER LANGUAGE plpgsql STABLE AS $$
DECLARE cursor_date DATE:=old_date; result INTEGER:=0; direction INTEGER:=CASE WHEN new_date<old_date THEN -1 ELSE 1 END;
BEGIN
 IF old_date IS NULL OR new_date IS NULL THEN RETURN 0; END IF;
 IF abs(new_date-old_date)>1096 OR old_date NOT BETWEEN DATE '2000-01-01' AND DATE '2100-12-31' OR new_date NOT BETWEEN DATE '2000-01-01' AND DATE '2100-12-31' THEN
  RAISE EXCEPTION 'Schedule is outside the supported working-day horizon.' USING ERRCODE='22023';
 END IF;
 WHILE cursor_date<>new_date LOOP
  cursor_date:=cursor_date+direction;
  IF projectpulse073_is_working_day(cursor_date) THEN result:=result+direction; END IF;
 END LOOP;
 RETURN result;
END; $$;
CREATE OR REPLACE FUNCTION projectpulse073_working_day_duration(start_date DATE,end_date DATE)
RETURNS INTEGER LANGUAGE plpgsql STABLE AS $$
DECLARE result INTEGER;
BEGIN
 IF start_date IS NULL OR end_date IS NULL OR end_date<start_date THEN RETURN 0; END IF;
 IF end_date-start_date>1096 OR start_date<DATE '2000-01-01' OR end_date>DATE '2100-12-31' THEN
  RAISE EXCEPTION 'Schedule is outside the supported working-day horizon.' USING ERRCODE='22023';
 END IF;
 SELECT count(*)::integer INTO result FROM generate_series(start_date,end_date,INTERVAL '1 day') day WHERE projectpulse073_is_working_day(day::date);
 RETURN result;
END; $$;

-- The legacy function signature is retained for callers, but its text input is
-- now an explicit user UUID selected from the role-filtered directory. No user
-- or authority records are created by intake review/commit.
CREATE OR REPLACE FUNCTION projectpulse055d4d_get_or_create_stakeholder_user(
    p_display_name text, p_role_code text, p_role_name text,
    p_job_title text, p_team_name text, p_actor_user_id uuid
) RETURNS uuid LANGUAGE plpgsql AS $$
DECLARE
    selected_user uuid;
    allowed_roles text[];
BEGIN
    IF btrim(COALESCE(p_display_name,''))='' THEN RETURN NULL; END IF;
    BEGIN
        selected_user := p_display_name::uuid;
    EXCEPTION WHEN invalid_text_representation THEN
        RAISE EXCEPTION 'Select the stakeholder from the active role-holder directory' USING ERRCODE='22023';
    END;
    allowed_roles := CASE upper(p_role_code)
        WHEN 'ACCOUNT_EXECUTIVE' THEN ARRAY['ACCOUNT_EXECUTIVE','ACCOUNT_EXECUTIVES','SALES','SALES_EXECUTIVE','AE']
        WHEN 'SOLUTION_ARCHITECT' THEN ARRAY['SOLUTION_ARCHITECT','SA','ARCHITECT','ANALYST_DEV_ARCHITECT']
        WHEN 'SOLUTION_ARCHITECT_ASSOCIATE' THEN ARRAY['SOLUTION_ARCHITECT_ASSOCIATE','SAA','INSIDE_SALES','SALES_SUPPORT','SALES']
        ELSE ARRAY[]::text[] END;
    IF NOT EXISTS (SELECT 1 FROM app_users u
        JOIN app_user_role_assignments a ON a.user_id=u.user_id AND a.is_active
        JOIN app_roles r ON r.app_role_id=a.app_role_id AND r.is_active
        WHERE u.user_id=selected_user AND u.is_active AND upper(r.role_code)=ANY(allowed_roles)) THEN
        RAISE EXCEPTION 'Stakeholder is inactive or does not hold the selected role' USING ERRCODE='22023';
    END IF;
    RETURN selected_user;
END $$;

INSERT INTO schema_migrations(migration_id,description) VALUES
 ('130_security_integrity_boundaries','Preserve document visibility; remove retired allocation grants; bound scheduling; provision profile and actor columns') ON CONFLICT DO NOTHING;
COMMIT;
