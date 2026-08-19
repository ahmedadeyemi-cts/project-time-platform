-- ProjectPulse migration 094
-- Shared project-planning collaboration access for Module 033 Project Forge and
-- Module 066 Project FlowHive.
--
-- Functional ownership remains with Project Managers and Project Manager Leads.
-- Engineers and Engineering Leads may review/edit planner content only inside
-- associated project scope. Account Executives and Solution Architects receive
-- associated-project read access only. View-As write restrictions remain an
-- application authority and are never weakened by this migration.

BEGIN;

DO $projectpulse094_prerequisites$
BEGIN
    IF to_regclass('public.projects') IS NULL
       OR to_regclass('public.project_tasks') IS NULL
       OR to_regclass('public.project_assignments') IS NULL
       OR to_regclass('public.app_users') IS NULL
       OR to_regclass('public.app_roles') IS NULL
       OR to_regclass('public.app_permissions') IS NULL
       OR to_regclass('public.app_role_permissions') IS NULL THEN
        RAISE EXCEPTION 'Migration 094 requires the canonical project, assignment, identity, and RBAC foundations.';
    END IF;
END;
$projectpulse094_prerequisites$;

CREATE TABLE IF NOT EXISTS project_planning_collaborators (
    project_planning_collaborator_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    collaboration_role VARCHAR(40) NOT NULL CHECK (collaboration_role IN (
        'project_manager_lead',
        'engineering_lead',
        'engineer',
        'technical_reviewer',
        'planner_editor',
        'stakeholder'
    )),
    access_level VARCHAR(24) NOT NULL CHECK (access_level IN (
        'view', 'review', 'edit', 'administer'
    )),
    reason VARCHAR(500) NOT NULL DEFAULT '',
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    effective_start_date DATE NOT NULL DEFAULT CURRENT_DATE,
    effective_end_date DATE NULL,
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    revision_number INTEGER NOT NULL DEFAULT 1 CHECK (revision_number >= 1),
    CONSTRAINT uq_project_planning_collaborator UNIQUE(project_id, user_id),
    CONSTRAINT ck_project_planning_collaborator_dates CHECK (
        effective_end_date IS NULL OR effective_end_date >= effective_start_date
    )
);

CREATE INDEX IF NOT EXISTS ix_project_planning_collaborators_user_scope
    ON project_planning_collaborators(user_id, is_active, effective_end_date, project_id);
CREATE INDEX IF NOT EXISTS ix_project_planning_collaborators_project_scope
    ON project_planning_collaborators(project_id, is_active, access_level, user_id);

CREATE TABLE IF NOT EXISTS project_planning_collaboration_audit_events (
    project_planning_collaboration_audit_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE RESTRICT,
    collaborator_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    event_code VARCHAR(48) NOT NULL CHECK (event_code IN (
        'collaborator_created', 'collaborator_updated', 'collaborator_deactivated'
    )),
    actual_actor_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    effective_actor_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    prior_state JSONB NULL,
    new_state JSONB NULL,
    reason VARCHAR(500) NOT NULL DEFAULT '',
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_project_planning_collaboration_audit_project
    ON project_planning_collaboration_audit_events(project_id, occurred_at DESC);

CREATE OR REPLACE FUNCTION projectpulse094_touch_collaborator()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse094_touch_collaborator_body$
BEGIN
    NEW.updated_at := NOW();
    NEW.revision_number := OLD.revision_number + 1;
    RETURN NEW;
END;
$projectpulse094_touch_collaborator_body$;

DROP TRIGGER IF EXISTS trg_project_planning_collaborator_touch_094
    ON project_planning_collaborators;
CREATE TRIGGER trg_project_planning_collaborator_touch_094
BEFORE UPDATE ON project_planning_collaborators
FOR EACH ROW EXECUTE FUNCTION projectpulse094_touch_collaborator();

CREATE OR REPLACE FUNCTION projectpulse094_record_collaborator_audit()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse094_record_collaborator_audit_body$
DECLARE
    prior JSONB := CASE WHEN TG_OP IN ('UPDATE','DELETE') THEN to_jsonb(OLD) ELSE NULL END;
    next JSONB := CASE WHEN TG_OP IN ('INSERT','UPDATE') THEN to_jsonb(NEW) ELSE NULL END;
    source JSONB := COALESCE(next, prior);
    event_name VARCHAR(48);
BEGIN
    event_name := CASE
        WHEN TG_OP = 'INSERT' THEN 'collaborator_created'
        WHEN TG_OP = 'DELETE' OR COALESCE((next->>'is_active')::BOOLEAN, FALSE) = FALSE
            THEN 'collaborator_deactivated'
        ELSE 'collaborator_updated'
    END;

    INSERT INTO project_planning_collaboration_audit_events(
        project_id,
        collaborator_user_id,
        event_code,
        actual_actor_user_id,
        effective_actor_user_id,
        prior_state,
        new_state,
        reason)
    VALUES(
        (source->>'project_id')::UUID,
        (source->>'user_id')::UUID,
        event_name,
        COALESCE(NULLIF(source->>'updated_by_user_id','')::UUID,
                 NULLIF(source->>'created_by_user_id','')::UUID),
        COALESCE(NULLIF(source->>'updated_by_user_id','')::UUID,
                 NULLIF(source->>'created_by_user_id','')::UUID),
        prior,
        next,
        COALESCE(source->>'reason',''));

    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
    RETURN NEW;
END;
$projectpulse094_record_collaborator_audit_body$;

DROP TRIGGER IF EXISTS trg_project_planning_collaborator_audit_094
    ON project_planning_collaborators;
CREATE TRIGGER trg_project_planning_collaborator_audit_094
AFTER INSERT OR UPDATE OR DELETE ON project_planning_collaborators
FOR EACH ROW EXECUTE FUNCTION projectpulse094_record_collaborator_audit();

CREATE OR REPLACE FUNCTION projectpulse094_json_uuid(source JSONB, key_name TEXT)
RETURNS UUID
LANGUAGE plpgsql
IMMUTABLE
AS $projectpulse094_json_uuid_body$
DECLARE
    candidate TEXT := NULLIF(BTRIM(COALESCE(source->>key_name, '')), '');
BEGIN
    IF candidate IS NULL OR candidate !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$' THEN
        RETURN NULL;
    END IF;
    RETURN candidate::UUID;
END;
$projectpulse094_json_uuid_body$;

CREATE OR REPLACE FUNCTION projectpulse094_has_role(candidate_user_id UUID, desired_codes TEXT[])
RETURNS BOOLEAN
LANGUAGE SQL
STABLE
AS $projectpulse094_has_role_body$
    SELECT EXISTS (
        SELECT 1
        FROM app_user_role_assignments assignment
        JOIN app_roles role
          ON role.app_role_id = assignment.app_role_id
         AND role.is_active = TRUE
        WHERE assignment.user_id = candidate_user_id
          AND assignment.is_active = TRUE
          AND trim(both '_' from regexp_replace(
                upper(btrim(COALESCE(role.role_code, ''))),
                '[^A-Z0-9]+',
                '_',
                'g')) = ANY(desired_codes)
    );
$projectpulse094_has_role_body$;

CREATE OR REPLACE FUNCTION projectpulse094_project_scope_reason(
    candidate_project_id UUID,
    candidate_user_id UUID)
RETURNS TEXT
LANGUAGE plpgsql
STABLE
AS $projectpulse094_project_scope_reason_body$
DECLARE
    project_row projects%ROWTYPE;
    project_json JSONB;
    project_manager_id UUID;
    account_executive_id UUID;
    solution_architect_id UUID;
    collaborator_level TEXT;
BEGIN
    SELECT * INTO project_row
    FROM projects
    WHERE project_id = candidate_project_id
      AND COALESCE(is_active, TRUE) = TRUE;

    IF NOT FOUND OR candidate_user_id IS NULL THEN
        RETURN '';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM app_users
        WHERE user_id = candidate_user_id
          AND COALESCE(is_active, TRUE) = TRUE
    ) THEN
        RETURN '';
    END IF;

    project_json := to_jsonb(project_row);
    project_manager_id := COALESCE(
        projectpulse094_json_uuid(project_json, 'project_manager_user_id'),
        projectpulse094_json_uuid(project_json, 'project_manager_id'),
        projectpulse094_json_uuid(project_json, 'pm_user_id'));
    account_executive_id := projectpulse094_json_uuid(project_json, 'account_executive_user_id');
    solution_architect_id := projectpulse094_json_uuid(project_json, 'solution_architect_user_id');

    IF projectpulse094_has_role(candidate_user_id, ARRAY[
        'SUPER_ADMINISTRATOR','SUPERADMINISTRATOR','GLOBAL_ADMINISTRATOR','GLOBALADMINISTRATOR'
    ]) THEN
        RETURN 'super_administrator';
    END IF;

    IF project_manager_id = candidate_user_id THEN
        RETURN 'assigned_project_manager';
    END IF;

    SELECT collaborator.access_level
    INTO collaborator_level
    FROM project_planning_collaborators collaborator
    WHERE collaborator.project_id = candidate_project_id
      AND collaborator.user_id = candidate_user_id
      AND collaborator.is_active = TRUE
      AND collaborator.effective_start_date <= CURRENT_DATE
      AND (collaborator.effective_end_date IS NULL OR collaborator.effective_end_date >= CURRENT_DATE)
    LIMIT 1;

    IF collaborator_level IS NOT NULL THEN
        RETURN 'planning_collaborator_' || collaborator_level;
    END IF;

    IF project_manager_id IS NOT NULL
       AND projectpulse094_has_role(candidate_user_id, ARRAY[
           'PROJECT_MANAGER_LEAD','PROJECT_MANAGEMENT_LEAD','PM_LEAD'
       ])
       AND EXISTS (
           SELECT 1
           FROM app_users project_manager
           WHERE project_manager.user_id = project_manager_id
             AND candidate_user_id = ANY(ARRAY[
                 projectpulse094_json_uuid(to_jsonb(project_manager), 'manager_user_id'),
                 projectpulse094_json_uuid(to_jsonb(project_manager), 'reports_to_user_id'),
                 projectpulse094_json_uuid(to_jsonb(project_manager), 'supervisor_user_id')
             ]::UUID[])
       ) THEN
        RETURN 'project_manager_lead_scope';
    END IF;

    IF account_executive_id = candidate_user_id THEN
        RETURN 'assigned_account_executive';
    END IF;

    IF solution_architect_id = candidate_user_id THEN
        RETURN 'assigned_solution_architect';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM project_assignments assignment
        JOIN project_tasks task
          ON task.task_id = assignment.task_id
         AND task.project_id = candidate_project_id
         AND COALESCE(task.is_active, TRUE) = TRUE
        WHERE assignment.user_id = candidate_user_id
          AND COALESCE(NULLIF(to_jsonb(assignment)->>'is_active','')::BOOLEAN, TRUE) = TRUE
          AND COALESCE(NULLIF(to_jsonb(assignment)->>'effective_start_date','')::DATE, CURRENT_DATE) <= CURRENT_DATE
          AND (
              NULLIF(to_jsonb(assignment)->>'effective_end_date','') IS NULL
              OR NULLIF(to_jsonb(assignment)->>'effective_end_date','')::DATE >= CURRENT_DATE
          )
    ) THEN
        RETURN 'active_project_assignment';
    END IF;

    IF projectpulse094_has_role(candidate_user_id, ARRAY[
        'ENGINEERING_LEAD','ENGINEERING_TEAM_LEAD'
    ]) AND EXISTS (
        SELECT 1
        FROM project_assignments assignment
        JOIN project_tasks task
          ON task.task_id = assignment.task_id
         AND task.project_id = candidate_project_id
         AND COALESCE(task.is_active, TRUE) = TRUE
        JOIN app_users engineer ON engineer.user_id = assignment.user_id
        WHERE COALESCE(NULLIF(to_jsonb(assignment)->>'is_active','')::BOOLEAN, TRUE) = TRUE
          AND candidate_user_id = ANY(ARRAY[
              projectpulse094_json_uuid(to_jsonb(engineer), 'manager_user_id'),
              projectpulse094_json_uuid(to_jsonb(engineer), 'reports_to_user_id'),
              projectpulse094_json_uuid(to_jsonb(engineer), 'supervisor_user_id'),
              projectpulse094_json_uuid(to_jsonb(engineer), 'engineering_lead_user_id')
          ]::UUID[])
    ) THEN
        RETURN 'engineering_lead_team_scope';
    END IF;

    RETURN '';
END;
$projectpulse094_project_scope_reason_body$;

CREATE OR REPLACE FUNCTION projectpulse094_can_view_project(candidate_project_id UUID, candidate_user_id UUID)
RETURNS BOOLEAN
LANGUAGE SQL
STABLE
AS $projectpulse094_can_view_project_body$
    SELECT projectpulse094_project_scope_reason(candidate_project_id, candidate_user_id) <> '';
$projectpulse094_can_view_project_body$;

CREATE OR REPLACE FUNCTION projectpulse094_can_edit_planner(candidate_project_id UUID, candidate_user_id UUID)
RETURNS BOOLEAN
LANGUAGE plpgsql
STABLE
AS $projectpulse094_can_edit_planner_body$
DECLARE
    reason TEXT := projectpulse094_project_scope_reason(candidate_project_id, candidate_user_id);
BEGIN
    IF reason IN (
        'super_administrator',
        'assigned_project_manager',
        'project_manager_lead_scope',
        'planning_collaborator_edit',
        'planning_collaborator_administer'
    ) THEN
        RETURN TRUE;
    END IF;

    IF reason IN (
        'active_project_assignment',
        'engineering_lead_team_scope',
        'planning_collaborator_review'
    ) THEN
        RETURN projectpulse094_has_role(candidate_user_id, ARRAY[
            'ENGINEER','ENGINEERING','ENGINEERING_LEAD','ENGINEERING_TEAM_LEAD',
            'SYSTEMS_ENGINEER','NETWORK_ENGINEER','ENTERPRISE_NETWORK_ENGINEER'
        ]);
    END IF;

    RETURN FALSE;
END;
$projectpulse094_can_edit_planner_body$;

CREATE OR REPLACE FUNCTION projectpulse094_can_administer_planner(candidate_project_id UUID, candidate_user_id UUID)
RETURNS BOOLEAN
LANGUAGE SQL
STABLE
AS $projectpulse094_can_administer_planner_body$
    SELECT projectpulse094_project_scope_reason(candidate_project_id, candidate_user_id) IN (
        'super_administrator',
        'assigned_project_manager',
        'project_manager_lead_scope',
        'planning_collaborator_administer'
    );
$projectpulse094_can_administer_planner_body$;

CREATE TABLE IF NOT EXISTS project_planning_094_permissions_created (
    app_permission_id UUID PRIMARY KEY REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    permission_code VARCHAR(100) NOT NULL UNIQUE,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS project_planning_094_role_grants (
    app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE RESTRICT,
    app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY(app_role_id, app_permission_id)
);

WITH desired(permission_code, permission_name, module_code, description) AS (
    VALUES
        ('VIEW_ASSOCIATED_PROJECT_FORGE_033','View associated Project Forge projects','033','View Project Forge only for projects associated through PM ownership, planning collaboration, engineering assignment, Account Executive ownership, or Solution Architect ownership.'),
        ('REVIEW_PROJECT_FORGE_PLAN_033','Review Project Forge plans','033','Review Project Forge planner content inside authorized project scope.'),
        ('EDIT_PROJECT_FORGE_REVIEW_PLAN_033','Edit Project Forge review plans','033','Edit review-plan content inside authorized project scope without adopting canonical tasks.'),
        ('MANAGE_PROJECT_FORGE_CANONICAL_TASKS_033','Manage Project Forge canonical tasks','033','Administer canonical Project Forge tasks for PM-governed project scope.'),
        ('ADOPT_PROJECT_FORGE_PLAN_033','Adopt Project Forge plans','033','Adopt an approved Project Forge review plan into canonical project tasks.'),
        ('VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066','View associated FlowHive projects','066','View FlowHive only for projects associated through PM ownership, planning collaboration, engineering assignment, Account Executive ownership, or Solution Architect ownership.'),
        ('REVIEW_FLOWHIVE_PLANNER_066','Review FlowHive planner','066','Review FlowHive planner content inside authorized project scope.'),
        ('EDIT_FLOWHIVE_PLANNER_066','Edit FlowHive planner','066','Edit the FlowHive working planner inside authorized project scope without changing PM-only controls.'),
        ('ADMINISTER_FLOWHIVE_PROJECT_066','Administer FlowHive project planning','066','Administer PM-governed FlowHive controls, formal reporting, baselines, and collaboration assignments.')
), inserted AS (
    INSERT INTO app_permissions(permission_code, permission_name, module_code, permission_description)
    SELECT permission_code, permission_name, module_code, description FROM desired
    ON CONFLICT(permission_code) DO NOTHING
    RETURNING app_permission_id, permission_code
)
INSERT INTO project_planning_094_permissions_created(app_permission_id, permission_code)
SELECT app_permission_id, permission_code FROM inserted
ON CONFLICT DO NOTHING;

WITH role_permission(role_code, permission_code) AS (
    VALUES
        ('PROJECT_MANAGER','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('PROJECT_MANAGER','REVIEW_PROJECT_FORGE_PLAN_033'),
        ('PROJECT_MANAGER','EDIT_PROJECT_FORGE_REVIEW_PLAN_033'),
        ('PROJECT_MANAGER','MANAGE_PROJECT_FORGE_CANONICAL_TASKS_033'),
        ('PROJECT_MANAGER','ADOPT_PROJECT_FORGE_PLAN_033'),
        ('PROJECT_MANAGER','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('PROJECT_MANAGER','REVIEW_FLOWHIVE_PLANNER_066'),
        ('PROJECT_MANAGER','EDIT_FLOWHIVE_PLANNER_066'),
        ('PROJECT_MANAGER','ADMINISTER_FLOWHIVE_PROJECT_066'),
        ('PROJECT_MANAGEMENT','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('PROJECT_MANAGEMENT','REVIEW_PROJECT_FORGE_PLAN_033'),
        ('PROJECT_MANAGEMENT','EDIT_PROJECT_FORGE_REVIEW_PLAN_033'),
        ('PROJECT_MANAGEMENT','MANAGE_PROJECT_FORGE_CANONICAL_TASKS_033'),
        ('PROJECT_MANAGEMENT','ADOPT_PROJECT_FORGE_PLAN_033'),
        ('PROJECT_MANAGEMENT','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('PROJECT_MANAGEMENT','REVIEW_FLOWHIVE_PLANNER_066'),
        ('PROJECT_MANAGEMENT','EDIT_FLOWHIVE_PLANNER_066'),
        ('PROJECT_MANAGEMENT','ADMINISTER_FLOWHIVE_PROJECT_066'),
        ('PROJECT_MANAGER_LEAD','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('PROJECT_MANAGER_LEAD','REVIEW_PROJECT_FORGE_PLAN_033'),
        ('PROJECT_MANAGER_LEAD','EDIT_PROJECT_FORGE_REVIEW_PLAN_033'),
        ('PROJECT_MANAGER_LEAD','MANAGE_PROJECT_FORGE_CANONICAL_TASKS_033'),
        ('PROJECT_MANAGER_LEAD','ADOPT_PROJECT_FORGE_PLAN_033'),
        ('PROJECT_MANAGER_LEAD','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('PROJECT_MANAGER_LEAD','REVIEW_FLOWHIVE_PLANNER_066'),
        ('PROJECT_MANAGER_LEAD','EDIT_FLOWHIVE_PLANNER_066'),
        ('PROJECT_MANAGER_LEAD','ADMINISTER_FLOWHIVE_PROJECT_066'),
        ('ENGINEER','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('ENGINEER','REVIEW_PROJECT_FORGE_PLAN_033'),
        ('ENGINEER','EDIT_PROJECT_FORGE_REVIEW_PLAN_033'),
        ('ENGINEER','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('ENGINEER','REVIEW_FLOWHIVE_PLANNER_066'),
        ('ENGINEER','EDIT_FLOWHIVE_PLANNER_066'),
        ('ENGINEERING','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('ENGINEERING','REVIEW_PROJECT_FORGE_PLAN_033'),
        ('ENGINEERING','EDIT_PROJECT_FORGE_REVIEW_PLAN_033'),
        ('ENGINEERING','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('ENGINEERING','REVIEW_FLOWHIVE_PLANNER_066'),
        ('ENGINEERING','EDIT_FLOWHIVE_PLANNER_066'),
        ('ENGINEERING_LEAD','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('ENGINEERING_LEAD','REVIEW_PROJECT_FORGE_PLAN_033'),
        ('ENGINEERING_LEAD','EDIT_PROJECT_FORGE_REVIEW_PLAN_033'),
        ('ENGINEERING_LEAD','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('ENGINEERING_LEAD','REVIEW_FLOWHIVE_PLANNER_066'),
        ('ENGINEERING_LEAD','EDIT_FLOWHIVE_PLANNER_066'),
        ('ACCOUNT_EXECUTIVE','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('ACCOUNT_EXECUTIVE','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('SALES_ACCOUNT_EXECUTIVE','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('SALES_ACCOUNT_EXECUTIVE','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('SOLUTION_ARCHITECT','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('SOLUTION_ARCHITECT','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('SOLUTIONS_ARCHITECT','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('SOLUTIONS_ARCHITECT','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066')
), candidates AS (
    SELECT role.app_role_id, permission.app_permission_id
    FROM role_permission desired
    JOIN app_roles role
      ON trim(both '_' from regexp_replace(upper(btrim(role.role_code)), '[^A-Z0-9]+', '_', 'g')) = desired.role_code
     AND role.is_active = TRUE
    JOIN app_permissions permission ON permission.permission_code = desired.permission_code
    LEFT JOIN app_role_permissions existing
      ON existing.app_role_id = role.app_role_id
     AND existing.app_permission_id = permission.app_permission_id
    WHERE existing.app_role_permission_id IS NULL
), inserted AS (
    INSERT INTO app_role_permissions(app_role_id, app_permission_id, created_at)
    SELECT app_role_id, app_permission_id, NOW() FROM candidates
    ON CONFLICT(app_role_id, app_permission_id) DO NOTHING
    RETURNING app_role_id, app_permission_id
)
INSERT INTO project_planning_094_role_grants(app_role_id, app_permission_id)
SELECT app_role_id, app_permission_id FROM inserted
ON CONFLICT DO NOTHING;

-- Preserve the modules' established view permissions so the role-aware module
-- directory can expose 033/066 while the project resolver still filters rows.
WITH desired_role(role_code) AS (
    VALUES
      ('PROJECT_MANAGER'),('PROJECT_MANAGEMENT'),('PROJECT_MANAGER_LEAD'),
      ('ENGINEER'),('ENGINEERING'),('ENGINEERING_LEAD'),('ENGINEERING_TEAM_LEAD'),
      ('ACCOUNT_EXECUTIVE'),('SALES_ACCOUNT_EXECUTIVE'),
      ('SOLUTION_ARCHITECT'),('SOLUTIONS_ARCHITECT')
), desired_permission(permission_code) AS (
    VALUES
      ('VIEW_PROJECT_FORGE_033'),
      ('VIEW_PROJECT_FLOWHIVE_066'),
      ('VIEW_FLOWHIVE_066')
), candidates AS (
    SELECT role.app_role_id, permission.app_permission_id
    FROM desired_role
    JOIN app_roles role
      ON trim(both '_' from regexp_replace(upper(btrim(role.role_code)), '[^A-Z0-9]+', '_', 'g')) = desired_role.role_code
     AND role.is_active = TRUE
    CROSS JOIN desired_permission
    JOIN app_permissions permission ON permission.permission_code = desired_permission.permission_code
    LEFT JOIN app_role_permissions existing
      ON existing.app_role_id = role.app_role_id
     AND existing.app_permission_id = permission.app_permission_id
    WHERE existing.app_role_permission_id IS NULL
), inserted AS (
    INSERT INTO app_role_permissions(app_role_id, app_permission_id, created_at)
    SELECT app_role_id, app_permission_id, NOW() FROM candidates
    ON CONFLICT(app_role_id, app_permission_id) DO NOTHING
    RETURNING app_role_id, app_permission_id
)
INSERT INTO project_planning_094_role_grants(app_role_id, app_permission_id)
SELECT app_role_id, app_permission_id FROM inserted
ON CONFLICT DO NOTHING;

CREATE TABLE IF NOT EXISTS project_planning_094_migration_evidence (
    migration_id VARCHAR(120) PRIMARY KEY,
    applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    policy_version VARCHAR(80) NOT NULL,
    ownership_does_not_grant_access BOOLEAN NOT NULL DEFAULT TRUE
);

INSERT INTO project_planning_094_migration_evidence(
    migration_id, policy_version, ownership_does_not_grant_access)
VALUES(
    '094_project_planning_collaboration_access',
    'PROJECT_PLANNING_COLLABORATION_V1',
    TRUE)
ON CONFLICT(migration_id) DO UPDATE
SET applied_at = NOW(),
    policy_version = EXCLUDED.policy_version,
    ownership_does_not_grant_access = TRUE;

COMMIT;
