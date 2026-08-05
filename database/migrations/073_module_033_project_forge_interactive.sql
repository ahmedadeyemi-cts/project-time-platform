-- ProjectPulse migration 073.
-- Module 033 Project Forge interactive task management, optimistic concurrency,
-- canonical dependencies, explicit Engineer review evidence, and working-day scheduling.
-- No sample projects, tasks, assignments, dependencies, people, or financial data are created.

BEGIN;

DO $projectpulse073_prerequisites$
BEGIN
    IF to_regclass('public.project_forge_plans') IS NULL
       OR to_regclass('public.project_forge_plan_tasks') IS NULL
       OR to_regclass('public.project_forge_plan_assignments') IS NULL
       OR to_regclass('public.project_forge_task_details') IS NULL
       OR to_regclass('public.project_tasks') IS NULL
       OR to_regclass('public.project_assignments') IS NULL
       OR to_regclass('public.company_holidays') IS NULL THEN
        RAISE EXCEPTION 'Project Forge interactive migration requires migration 070 and the canonical project/calendar foundations.';
    END IF;
END;
$projectpulse073_prerequisites$;

-- A fresh migration owns the interactive columns below. Refuse to reuse a
-- concurrently introduced column/table so rollback can never remove schema
-- that migration 073 did not create. Reapplying registered migration 073 is
-- intentionally idempotent.
DO $projectpulse073_ownership_preflight$
BEGIN
    IF NOT EXISTS(
        SELECT 1 FROM schema_migrations
        WHERE migration_id='073_module_033_project_forge_interactive'
    ) AND (
        EXISTS(
            SELECT 1 FROM information_schema.columns
            WHERE table_schema='public' AND (
                (table_name='project_tasks' AND column_name IN ('revision_number','updated_by_user_id'))
                OR (table_name='project_forge_task_details' AND column_name IN (
                    'parent_task_id','duration_working_days','display_order','constraint_type','constraint_date','blocked_reason'
                ))
                OR (table_name='project_forge_plan_tasks' AND column_name='blocked_reason')
                OR (table_name='project_assignments' AND column_name IN ('is_primary_assignee','revision_number','updated_by_user_id'))
                OR (table_name='project_forge_plan_assignments' AND column_name='reviewed_task_revision')
            )
        )
        OR to_regclass('public.project_task_dependencies') IS NOT NULL
        OR to_regclass('public.project_forge_073_permissions_created') IS NOT NULL
        OR to_regclass('public.project_forge_073_role_grants') IS NOT NULL
    ) THEN
        RAISE EXCEPTION 'Migration 073 ownership preflight failed: an interactive Project Forge column or table already exists without migration evidence.';
    END IF;
END;
$projectpulse073_ownership_preflight$;

ALTER TABLE project_tasks
    ADD COLUMN IF NOT EXISTS revision_number INTEGER NOT NULL DEFAULT 1 CHECK (revision_number >= 1),
    ADD COLUMN IF NOT EXISTS updated_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL;

ALTER TABLE project_forge_task_details
    ADD COLUMN IF NOT EXISTS parent_task_id UUID NULL REFERENCES project_tasks(task_id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS duration_working_days INTEGER NOT NULL DEFAULT 0 CHECK (duration_working_days BETWEEN 0 AND 730),
    ADD COLUMN IF NOT EXISTS display_order INTEGER NOT NULL DEFAULT 0 CHECK (display_order >= 0),
    ADD COLUMN IF NOT EXISTS constraint_type VARCHAR(24) NOT NULL DEFAULT 'ASAP' CHECK (constraint_type IN ('ASAP','ALAP','MSO','MFO','SNET','SNLT','FNET','FNLT')),
    ADD COLUMN IF NOT EXISTS constraint_date DATE NULL,
    ADD COLUMN IF NOT EXISTS blocked_reason TEXT NOT NULL DEFAULT '';

ALTER TABLE project_forge_plan_tasks
    ADD COLUMN IF NOT EXISTS blocked_reason TEXT NOT NULL DEFAULT '';

DO $projectpulse073_constraint_preflight$
BEGIN
    IF EXISTS(SELECT 1 FROM project_forge_plan_tasks WHERE duration_working_days>730) THEN
        RAISE EXCEPTION 'Project Forge interactive migration requires all review-plan task durations to be 730 working days or fewer.';
    END IF;
    IF EXISTS(SELECT 1 FROM project_forge_task_dependencies WHERE lag_working_days NOT BETWEEN -365 AND 365) THEN
        RAISE EXCEPTION 'Project Forge interactive migration requires dependency lead/lag values between -365 and 365 working days.';
    END IF;
END;
$projectpulse073_constraint_preflight$;

ALTER TABLE project_forge_plan_tasks
    DROP CONSTRAINT IF EXISTS project_forge_plan_tasks_duration_working_days_check;
ALTER TABLE project_forge_plan_tasks
    ADD CONSTRAINT project_forge_plan_tasks_duration_working_days_check
    CHECK (duration_working_days IS NULL OR duration_working_days BETWEEN 0 AND 730);

ALTER TABLE project_forge_task_dependencies
    DROP CONSTRAINT IF EXISTS project_forge_task_dependencies_lag_working_days_check;
ALTER TABLE project_forge_task_dependencies
    ADD CONSTRAINT project_forge_task_dependencies_lag_working_days_check
    CHECK (lag_working_days BETWEEN -365 AND 365);

ALTER TABLE project_assignments
    ADD COLUMN IF NOT EXISTS is_primary_assignee BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS revision_number INTEGER NOT NULL DEFAULT 1 CHECK (revision_number >= 1),
    ADD COLUMN IF NOT EXISTS updated_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL;

ALTER TABLE project_forge_plan_assignments
    ADD COLUMN IF NOT EXISTS reviewed_task_revision INTEGER NULL CHECK (reviewed_task_revision IS NULL OR reviewed_task_revision >= 1);

WITH ranked AS (
    SELECT project_assignment_id,
           row_number() OVER (
               PARTITION BY task_id
               ORDER BY (effective_start_date<=CURRENT_DATE AND (effective_end_date IS NULL OR effective_end_date>=CURRENT_DATE)) DESC,
                        effective_start_date DESC,project_assignment_id
           ) AS row_number
    FROM project_assignments
)
UPDATE project_assignments assignment
SET is_primary_assignee=TRUE
FROM ranked
WHERE ranked.project_assignment_id=assignment.project_assignment_id
  AND ranked.row_number=1
  AND assignment.is_primary_assignee=FALSE;

CREATE UNIQUE INDEX IF NOT EXISTS ux_project_assignments_primary_task
    ON project_assignments(task_id)
    WHERE is_primary_assignee=TRUE;
CREATE INDEX IF NOT EXISTS ix_project_forge_task_details_project_lane_order
    ON project_forge_task_details(project_id,kanban_category,display_order,task_id);

CREATE TABLE IF NOT EXISTS project_task_dependencies (
    project_task_dependency_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    predecessor_task_id UUID NOT NULL REFERENCES project_tasks(task_id) ON DELETE CASCADE,
    successor_task_id UUID NOT NULL REFERENCES project_tasks(task_id) ON DELETE CASCADE,
    dependency_type VARCHAR(8) NOT NULL DEFAULT 'FS' CHECK (dependency_type IN ('FS','SS','FF','SF')),
    lag_working_days INTEGER NOT NULL DEFAULT 0 CHECK (lag_working_days BETWEEN -365 AND 365),
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    revision_number INTEGER NOT NULL DEFAULT 1 CHECK (revision_number >= 1),
    CONSTRAINT ck_project_task_dependency_not_self CHECK (predecessor_task_id<>successor_task_id),
    CONSTRAINT uq_project_task_dependency UNIQUE(project_id,predecessor_task_id,successor_task_id,dependency_type)
);

CREATE INDEX IF NOT EXISTS ix_project_task_dependencies_successor
    ON project_task_dependencies(project_id,successor_task_id);

CREATE OR REPLACE FUNCTION projectpulse073_validate_canonical_dependency()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse073_validate_canonical_dependency$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM project_tasks predecessor
        WHERE predecessor.task_id=NEW.predecessor_task_id AND predecessor.project_id=NEW.project_id AND predecessor.is_active=TRUE
    ) OR NOT EXISTS (
        SELECT 1 FROM project_tasks successor
        WHERE successor.task_id=NEW.successor_task_id AND successor.project_id=NEW.project_id AND successor.is_active=TRUE
    ) THEN
        RAISE EXCEPTION 'Canonical dependency tasks must be active and belong to the same project.';
    END IF;

    IF EXISTS (
        WITH RECURSIVE reachable(task_id) AS (
            SELECT NEW.successor_task_id
            UNION
            SELECT dependency.successor_task_id
            FROM project_task_dependencies dependency
            JOIN reachable current_node ON current_node.task_id=dependency.predecessor_task_id
            WHERE dependency.project_task_dependency_id<>NEW.project_task_dependency_id
        )
        SELECT 1 FROM reachable WHERE task_id=NEW.predecessor_task_id
    ) THEN
        RAISE EXCEPTION 'Canonical task dependency would create a cycle.';
    END IF;
    RETURN NEW;
END;
$projectpulse073_validate_canonical_dependency$;

CREATE OR REPLACE FUNCTION projectpulse073_validate_parent_task()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse073_validate_parent_task$
BEGIN
    IF NEW.parent_task_id=NEW.task_id THEN
        RAISE EXCEPTION 'A task cannot be its own parent.';
    END IF;
    IF NEW.parent_task_id IS NOT NULL AND NOT EXISTS(
        SELECT 1 FROM project_tasks parent
        WHERE parent.task_id=NEW.parent_task_id AND parent.project_id=NEW.project_id AND parent.is_active=TRUE
    ) THEN
        RAISE EXCEPTION 'A parent task must be active and belong to the same project.';
    END IF;
    IF NEW.parent_task_id IS NOT NULL AND EXISTS(
        WITH RECURSIVE ancestors(task_id,parent_task_id) AS (
            SELECT detail.task_id,detail.parent_task_id
            FROM project_forge_task_details detail WHERE detail.task_id=NEW.parent_task_id
            UNION
            SELECT detail.task_id,detail.parent_task_id
            FROM project_forge_task_details detail JOIN ancestors prior ON detail.task_id=prior.parent_task_id
        )
        SELECT 1 FROM ancestors WHERE task_id=NEW.task_id OR parent_task_id=NEW.task_id
    ) THEN
        RAISE EXCEPTION 'A parent-task relationship would create a hierarchy cycle.';
    END IF;
    RETURN NEW;
END;
$projectpulse073_validate_parent_task$;

CREATE OR REPLACE FUNCTION projectpulse073_record_dependency_audit()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse073_record_dependency_audit$
DECLARE
    source_row JSONB := CASE WHEN TG_OP='DELETE' THEN to_jsonb(OLD) ELSE to_jsonb(NEW) END;
BEGIN
    INSERT INTO project_forge_audit_events(
        audit_event_id,project_id,event_code,entity_type,entity_id,actual_actor_user_id,effective_actor_user_id,
        prior_state,new_state,event_metadata,correlation_id)
    VALUES(
        gen_random_uuid(),(source_row->>'project_id')::uuid,'canonical_dependency_'||lower(TG_OP),'canonical_task_dependency',
        (source_row->>'project_task_dependency_id')::uuid,NULLIF(source_row->>'updated_by_user_id','')::uuid,
        NULLIF(source_row->>'updated_by_user_id','')::uuid,
        CASE WHEN TG_OP IN ('UPDATE','DELETE') THEN to_jsonb(OLD) ELSE NULL END,
        CASE WHEN TG_OP IN ('INSERT','UPDATE') THEN to_jsonb(NEW) ELSE NULL END,'{}'::jsonb,'');
    IF TG_OP='DELETE' THEN RETURN OLD; END IF;
    RETURN NEW;
END;
$projectpulse073_record_dependency_audit$;

DROP TRIGGER IF EXISTS trg_project_tasks_revision_073 ON project_tasks;
CREATE TRIGGER trg_project_tasks_revision_073
BEFORE UPDATE ON project_tasks
FOR EACH ROW EXECUTE FUNCTION projectpulse070_touch_revision();

DROP TRIGGER IF EXISTS trg_project_assignments_revision_073 ON project_assignments;
CREATE TRIGGER trg_project_assignments_revision_073
BEFORE UPDATE ON project_assignments
FOR EACH ROW EXECUTE FUNCTION projectpulse070_touch_revision();

DROP TRIGGER IF EXISTS trg_project_task_dependencies_validate_073 ON project_task_dependencies;
CREATE TRIGGER trg_project_task_dependencies_validate_073
BEFORE INSERT OR UPDATE ON project_task_dependencies
FOR EACH ROW EXECUTE FUNCTION projectpulse073_validate_canonical_dependency();
DROP TRIGGER IF EXISTS trg_project_task_dependencies_revision_073 ON project_task_dependencies;
CREATE TRIGGER trg_project_task_dependencies_revision_073
BEFORE UPDATE ON project_task_dependencies
FOR EACH ROW EXECUTE FUNCTION projectpulse070_touch_revision();
DROP TRIGGER IF EXISTS trg_project_task_dependencies_audit_073 ON project_task_dependencies;
CREATE TRIGGER trg_project_task_dependencies_audit_073
AFTER INSERT OR UPDATE OR DELETE ON project_task_dependencies
FOR EACH ROW EXECUTE FUNCTION projectpulse073_record_dependency_audit();

DROP TRIGGER IF EXISTS trg_project_forge_task_details_parent_073 ON project_forge_task_details;
CREATE TRIGGER trg_project_forge_task_details_parent_073
BEFORE INSERT OR UPDATE OF parent_task_id,project_id ON project_forge_task_details
FOR EACH ROW EXECUTE FUNCTION projectpulse073_validate_parent_task();

CREATE OR REPLACE FUNCTION projectpulse073_is_working_day(candidate DATE)
RETURNS BOOLEAN
LANGUAGE SQL
STABLE
AS $projectpulse073_is_working_day$
    SELECT candidate IS NOT NULL
       AND EXTRACT(ISODOW FROM candidate) BETWEEN 1 AND 5
       AND NOT EXISTS(
           SELECT 1 FROM company_holidays holiday
           WHERE holiday.holiday_date=candidate AND holiday.is_active=TRUE AND holiday.is_floating_holiday=FALSE
       );
$projectpulse073_is_working_day$;

CREATE OR REPLACE FUNCTION projectpulse073_add_working_days(source_date DATE, working_days INTEGER)
RETURNS DATE
LANGUAGE plpgsql
STABLE
AS $projectpulse073_add_working_days$
DECLARE
    result_date DATE := source_date;
    remaining INTEGER := abs(COALESCE(working_days,0));
    direction INTEGER := CASE WHEN COALESCE(working_days,0)<0 THEN -1 ELSE 1 END;
BEGIN
    IF source_date IS NULL THEN RETURN NULL; END IF;
    WHILE remaining>0 LOOP
        result_date := result_date+direction;
        IF projectpulse073_is_working_day(result_date) THEN remaining := remaining-1; END IF;
    END LOOP;
    RETURN result_date;
END;
$projectpulse073_add_working_days$;

CREATE OR REPLACE FUNCTION projectpulse073_working_day_delta(old_date DATE, new_date DATE)
RETURNS INTEGER
LANGUAGE plpgsql
STABLE
AS $projectpulse073_working_day_delta$
DECLARE
    cursor_date DATE := old_date;
    result INTEGER := 0;
    direction INTEGER := CASE WHEN new_date<old_date THEN -1 ELSE 1 END;
BEGIN
    IF old_date IS NULL OR new_date IS NULL OR old_date=new_date THEN RETURN 0; END IF;
    WHILE cursor_date<>new_date LOOP
        cursor_date := cursor_date+direction;
        IF projectpulse073_is_working_day(cursor_date) THEN result := result+direction; END IF;
    END LOOP;
    RETURN result;
END;
$projectpulse073_working_day_delta$;

CREATE OR REPLACE FUNCTION projectpulse073_working_day_duration(start_date DATE, end_date DATE)
RETURNS INTEGER
LANGUAGE SQL
STABLE
AS $projectpulse073_working_day_duration$
    SELECT CASE WHEN start_date IS NULL OR end_date IS NULL OR end_date<start_date THEN 0 ELSE COUNT(*)::integer END
    FROM generate_series(start_date,end_date,INTERVAL '1 day') day
    WHERE projectpulse073_is_working_day(day::date);
$projectpulse073_working_day_duration$;

CREATE TABLE IF NOT EXISTS project_forge_073_permissions_created (
    app_permission_id UUID PRIMARY KEY REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    permission_code VARCHAR(100) NOT NULL UNIQUE,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE IF NOT EXISTS project_forge_073_role_grants (
    app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE RESTRICT,
    app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY(app_role_id,app_permission_id)
);

WITH inserted AS (
    INSERT INTO app_permissions(permission_code,permission_name,module_code,permission_description)
    VALUES('UPDATE_ASSIGNED_PROJECT_FORGE_TASK_STATUS_033','Update assigned Project Forge task status','033',
           'Move and update workflow status/progress for an actively assigned canonical Project Forge task; no dates, costs, dependencies, or assignments.')
    ON CONFLICT(permission_code) DO NOTHING
    RETURNING app_permission_id,permission_code
)
INSERT INTO project_forge_073_permissions_created(app_permission_id,permission_code)
SELECT app_permission_id,permission_code FROM inserted ON CONFLICT DO NOTHING;

WITH desired(role_code) AS (
    VALUES('ENGINEERING'),('ENGINEER'),('ENGINEERING_LEAD'),('ENGINEERING_TEAM_LEAD'),
          ('SYSTEMS_ENGINEER'),('NETWORK_ENGINEER'),('ENTERPRISE_NETWORK_ENGINEER')
), candidates AS (
    SELECT role.app_role_id,permission.app_permission_id
    FROM desired
    JOIN app_roles role ON UPPER(role.role_code)=desired.role_code AND role.is_active=TRUE
    JOIN app_permissions permission ON permission.permission_code='UPDATE_ASSIGNED_PROJECT_FORGE_TASK_STATUS_033'
    LEFT JOIN app_role_permissions existing ON existing.app_role_id=role.app_role_id AND existing.app_permission_id=permission.app_permission_id
    WHERE existing.app_role_permission_id IS NULL
), inserted AS (
    INSERT INTO app_role_permissions(app_role_id,app_permission_id,created_at)
    SELECT app_role_id,app_permission_id,NOW() FROM candidates
    ON CONFLICT(app_role_id,app_permission_id) DO NOTHING
    RETURNING app_role_id,app_permission_id
)
INSERT INTO project_forge_073_role_grants(app_role_id,app_permission_id)
SELECT app_role_id,app_permission_id FROM inserted ON CONFLICT DO NOTHING;

INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES('073_module_033_project_forge_interactive',
       'Add Module 033 interactive task revisions, canonical dependencies, primary assignments, review revision evidence, RBAC, and holiday-aware schedule helpers',NOW())
ON CONFLICT(migration_id) DO NOTHING;

COMMIT;
