-- ProjectPulse migration 095
-- Project-scoped collaboration for Module 033 Project Forge and Module 066 Project FlowHive.
--
-- Functional ownership remains with the assigned Project Manager / PM Lead.
-- Engineers and Engineering Leads may review and edit planning content only for
-- associated projects. Account Executives and Solution Architects may view only
-- projects where the canonical project record names them. Module Management owner
-- metadata is intentionally untouched and never grants access.

BEGIN;

DO $projectpulse095_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.projects') IS NULL
       OR to_regclass('public.project_assignments') IS NULL
       OR to_regclass('public.app_users') IS NULL
       OR to_regclass('public.app_roles') IS NULL
       OR to_regclass('public.app_permissions') IS NULL
       OR to_regclass('public.app_role_permissions') IS NULL
       OR to_regclass('public.project_forge_plans') IS NULL
       OR to_regclass('public.project_flowhive_plans') IS NULL THEN
        RAISE EXCEPTION 'Migration 095 requires canonical project, identity, RBAC, Project Forge, and Project FlowHive foundations.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema='public'
          AND table_name='projects'
          AND column_name='account_executive_user_id'
    ) OR NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema='public'
          AND table_name='projects'
          AND column_name='solution_architect_user_id'
    ) THEN
        RAISE EXCEPTION 'Migration 095 requires canonical projects.account_executive_user_id and projects.solution_architect_user_id.';
    END IF;
END;
$projectpulse095_prerequisites$;

CREATE TABLE IF NOT EXISTS project_planning_collaborators (
    project_planning_collaborator_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    module_code VARCHAR(16) NOT NULL CHECK (module_code IN ('033','066')),
    collaboration_level VARCHAR(24) NOT NULL CHECK (collaboration_level IN ('viewer','reviewer','editor')),
    assigned_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    effective_start_date DATE NOT NULL DEFAULT CURRENT_DATE,
    effective_end_date DATE NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    notes TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_project_planning_collaborator UNIQUE(project_id,user_id,module_code),
    CONSTRAINT ck_project_planning_collaborator_dates CHECK (
        effective_end_date IS NULL OR effective_end_date >= effective_start_date
    )
);

CREATE INDEX IF NOT EXISTS ix_project_planning_collaborators_user_scope
    ON project_planning_collaborators(user_id,module_code,is_active,effective_start_date,effective_end_date);
CREATE INDEX IF NOT EXISTS ix_project_planning_collaborators_project_scope
    ON project_planning_collaborators(project_id,module_code,is_active);

CREATE TABLE IF NOT EXISTS project_planning_collaboration_audit_events (
    project_planning_collaboration_audit_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_planning_collaborator_id UUID NULL,
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    module_code VARCHAR(16) NOT NULL,
    event_code VARCHAR(80) NOT NULL,
    actor_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    prior_state JSONB NULL,
    new_state JSONB NULL,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_project_planning_collaboration_audit_project
    ON project_planning_collaboration_audit_events(project_id,occurred_at DESC);

CREATE OR REPLACE FUNCTION projectpulse095_touch_planning_collaborator()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse095_touch_planning_collaborator_body$
BEGIN
    NEW.updated_at := NOW();
    RETURN NEW;
END;
$projectpulse095_touch_planning_collaborator_body$;

CREATE OR REPLACE FUNCTION projectpulse095_audit_planning_collaborator()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse095_audit_planning_collaborator_body$
DECLARE
    source_row JSONB := CASE WHEN TG_OP='DELETE' THEN to_jsonb(OLD) ELSE to_jsonb(NEW) END;
BEGIN
    INSERT INTO project_planning_collaboration_audit_events(
        project_planning_collaborator_id,
        project_id,
        user_id,
        module_code,
        event_code,
        actor_user_id,
        prior_state,
        new_state
    )
    VALUES(
        NULLIF(source_row->>'project_planning_collaborator_id','')::UUID,
        (source_row->>'project_id')::UUID,
        (source_row->>'user_id')::UUID,
        source_row->>'module_code',
        'PROJECT_PLANNING_COLLABORATOR_'||TG_OP,
        NULLIF(source_row->>'assigned_by_user_id','')::UUID,
        CASE WHEN TG_OP IN ('UPDATE','DELETE') THEN to_jsonb(OLD) ELSE NULL END,
        CASE WHEN TG_OP IN ('INSERT','UPDATE') THEN to_jsonb(NEW) ELSE NULL END
    );
    RETURN CASE WHEN TG_OP='DELETE' THEN OLD ELSE NEW END;
END;
$projectpulse095_audit_planning_collaborator_body$;

CREATE OR REPLACE FUNCTION projectpulse095_block_collaboration_audit_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse095_block_collaboration_audit_mutation_body$
BEGIN
    RAISE EXCEPTION 'Project planning collaboration audit evidence is append-only.';
END;
$projectpulse095_block_collaboration_audit_mutation_body$;

DROP TRIGGER IF EXISTS trg_project_planning_collaborator_touch_095 ON project_planning_collaborators;
CREATE TRIGGER trg_project_planning_collaborator_touch_095
BEFORE UPDATE ON project_planning_collaborators
FOR EACH ROW EXECUTE FUNCTION projectpulse095_touch_planning_collaborator();

DROP TRIGGER IF EXISTS trg_project_planning_collaborator_audit_095 ON project_planning_collaborators;
CREATE TRIGGER trg_project_planning_collaborator_audit_095
AFTER INSERT OR UPDATE OR DELETE ON project_planning_collaborators
FOR EACH ROW EXECUTE FUNCTION projectpulse095_audit_planning_collaborator();

DROP TRIGGER IF EXISTS trg_project_planning_collaboration_audit_immutable_095 ON project_planning_collaboration_audit_events;
CREATE TRIGGER trg_project_planning_collaboration_audit_immutable_095
BEFORE UPDATE OR DELETE ON project_planning_collaboration_audit_events
FOR EACH ROW EXECUTE FUNCTION projectpulse095_block_collaboration_audit_mutation();

CREATE TABLE IF NOT EXISTS project_planning_095_permissions_created (
    app_permission_id UUID PRIMARY KEY REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    permission_code VARCHAR(120) NOT NULL UNIQUE,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS project_planning_095_role_grants (
    app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE RESTRICT,
    app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY(app_role_id,app_permission_id)
);

WITH inserted AS (
    INSERT INTO app_permissions(permission_code,permission_name,module_code,permission_description)
    VALUES
        ('VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066','View associated FlowHive project','066','View FlowHive only for projects associated through PM ownership, an active project assignment, engineering-team scope, explicit planning collaboration, Account Executive ownership, or Solution Architect ownership.'),
        ('REVIEW_FLOWHIVE_PLANNER_066','Review FlowHive planner','066','Review project-scoped FlowHive planning content and technical evidence without receiving PM governance, financial, baseline, or customer-sharing authority.'),
        ('EDIT_FLOWHIVE_PLANNER_066','Edit FlowHive planner','066','Edit and save project-scoped FlowHive planning working copies and review versions without receiving financial, baseline, or customer-sharing authority.'),
        ('VIEW_ASSOCIATED_PROJECT_FORGE_033','View associated Project Forge project','033','View Project Forge only for projects associated through PM ownership, an active project assignment, engineering-team scope, explicit planning collaboration, Account Executive ownership, or Solution Architect ownership.'),
        ('REVIEW_PROJECT_FORGE_PLAN_033','Review Project Forge plan','033','Review a project-scoped Project Forge review plan and complete assigned technical review without canonical-task adoption authority.'),
        ('EDIT_PROJECT_FORGE_REVIEW_PLAN_033','Edit Project Forge review plan','033','Edit project-scoped Project Forge review-plan content without canonical task administration, plan adoption, financial, or customer-delivery authority.')
    ON CONFLICT(permission_code) DO NOTHING
    RETURNING app_permission_id,permission_code
)
INSERT INTO project_planning_095_permissions_created(app_permission_id,permission_code)
SELECT app_permission_id,permission_code FROM inserted
ON CONFLICT DO NOTHING;

WITH desired(role_code,permission_code) AS (
    VALUES
        ('SUPER_ADMINISTRATOR','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('SUPER_ADMINISTRATOR','REVIEW_FLOWHIVE_PLANNER_066'),
        ('SUPER_ADMINISTRATOR','EDIT_FLOWHIVE_PLANNER_066'),
        ('SUPER_ADMINISTRATOR','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('SUPER_ADMINISTRATOR','REVIEW_PROJECT_FORGE_PLAN_033'),
        ('SUPER_ADMINISTRATOR','EDIT_PROJECT_FORGE_REVIEW_PLAN_033'),
        ('SYSTEM_ADMINISTRATOR','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('SYSTEM_ADMINISTRATOR','REVIEW_FLOWHIVE_PLANNER_066'),
        ('SYSTEM_ADMINISTRATOR','EDIT_FLOWHIVE_PLANNER_066'),
        ('SYSTEM_ADMINISTRATOR','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('SYSTEM_ADMINISTRATOR','REVIEW_PROJECT_FORGE_PLAN_033'),
        ('SYSTEM_ADMINISTRATOR','EDIT_PROJECT_FORGE_REVIEW_PLAN_033'),
        ('ADMINISTRATOR','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('ADMINISTRATOR','REVIEW_FLOWHIVE_PLANNER_066'),
        ('ADMINISTRATOR','EDIT_FLOWHIVE_PLANNER_066'),
        ('ADMINISTRATOR','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('ADMINISTRATOR','REVIEW_PROJECT_FORGE_PLAN_033'),
        ('ADMINISTRATOR','EDIT_PROJECT_FORGE_REVIEW_PLAN_033'),
        ('PROJECT_MANAGER','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('PROJECT_MANAGER','REVIEW_FLOWHIVE_PLANNER_066'),
        ('PROJECT_MANAGER','EDIT_FLOWHIVE_PLANNER_066'),
        ('PROJECT_MANAGER','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('PROJECT_MANAGER','REVIEW_PROJECT_FORGE_PLAN_033'),
        ('PROJECT_MANAGER','EDIT_PROJECT_FORGE_REVIEW_PLAN_033'),
        ('PROJECT_MANAGEMENT','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('PROJECT_MANAGEMENT','REVIEW_FLOWHIVE_PLANNER_066'),
        ('PROJECT_MANAGEMENT','EDIT_FLOWHIVE_PLANNER_066'),
        ('PROJECT_MANAGEMENT','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('PROJECT_MANAGEMENT','REVIEW_PROJECT_FORGE_PLAN_033'),
        ('PROJECT_MANAGEMENT','EDIT_PROJECT_FORGE_REVIEW_PLAN_033'),
        ('PROJECT_MANAGEMENT_LEAD','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('PROJECT_MANAGEMENT_LEAD','REVIEW_FLOWHIVE_PLANNER_066'),
        ('PROJECT_MANAGEMENT_LEAD','EDIT_FLOWHIVE_PLANNER_066'),
        ('PROJECT_MANAGEMENT_LEAD','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('PROJECT_MANAGEMENT_LEAD','REVIEW_PROJECT_FORGE_PLAN_033'),
        ('PROJECT_MANAGEMENT_LEAD','EDIT_PROJECT_FORGE_REVIEW_PLAN_033'),
        ('PROJECT_MANAGEMENT_TEAM_LEAD','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('PROJECT_MANAGEMENT_TEAM_LEAD','REVIEW_FLOWHIVE_PLANNER_066'),
        ('PROJECT_MANAGEMENT_TEAM_LEAD','EDIT_FLOWHIVE_PLANNER_066'),
        ('PROJECT_MANAGEMENT_TEAM_LEAD','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('PROJECT_MANAGEMENT_TEAM_LEAD','REVIEW_PROJECT_FORGE_PLAN_033'),
        ('PROJECT_MANAGEMENT_TEAM_LEAD','EDIT_PROJECT_FORGE_REVIEW_PLAN_033'),
        ('PM_TEAM_LEAD','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('PM_TEAM_LEAD','REVIEW_FLOWHIVE_PLANNER_066'),
        ('PM_TEAM_LEAD','EDIT_FLOWHIVE_PLANNER_066'),
        ('PM_TEAM_LEAD','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('PM_TEAM_LEAD','REVIEW_PROJECT_FORGE_PLAN_033'),
        ('PM_TEAM_LEAD','EDIT_PROJECT_FORGE_REVIEW_PLAN_033'),
        ('ENGINEER','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('ENGINEER','REVIEW_FLOWHIVE_PLANNER_066'),
        ('ENGINEER','EDIT_FLOWHIVE_PLANNER_066'),
        ('ENGINEER','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('ENGINEER','REVIEW_PROJECT_FORGE_PLAN_033'),
        ('ENGINEER','EDIT_PROJECT_FORGE_REVIEW_PLAN_033'),
        ('ENGINEERING','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('ENGINEERING','REVIEW_FLOWHIVE_PLANNER_066'),
        ('ENGINEERING','EDIT_FLOWHIVE_PLANNER_066'),
        ('ENGINEERING','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('ENGINEERING','REVIEW_PROJECT_FORGE_PLAN_033'),
        ('ENGINEERING','EDIT_PROJECT_FORGE_REVIEW_PLAN_033'),
        ('ENGINEERING_LEAD','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('ENGINEERING_LEAD','REVIEW_FLOWHIVE_PLANNER_066'),
        ('ENGINEERING_LEAD','EDIT_FLOWHIVE_PLANNER_066'),
        ('ENGINEERING_LEAD','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('ENGINEERING_LEAD','REVIEW_PROJECT_FORGE_PLAN_033'),
        ('ENGINEERING_LEAD','EDIT_PROJECT_FORGE_REVIEW_PLAN_033'),
        ('ENGINEERING_TEAM_LEAD','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('ENGINEERING_TEAM_LEAD','REVIEW_FLOWHIVE_PLANNER_066'),
        ('ENGINEERING_TEAM_LEAD','EDIT_FLOWHIVE_PLANNER_066'),
        ('ENGINEERING_TEAM_LEAD','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('ENGINEERING_TEAM_LEAD','REVIEW_PROJECT_FORGE_PLAN_033'),
        ('ENGINEERING_TEAM_LEAD','EDIT_PROJECT_FORGE_REVIEW_PLAN_033'),
        ('SYSTEMS_ENGINEER','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('SYSTEMS_ENGINEER','REVIEW_FLOWHIVE_PLANNER_066'),
        ('SYSTEMS_ENGINEER','EDIT_FLOWHIVE_PLANNER_066'),
        ('SYSTEMS_ENGINEER','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('SYSTEMS_ENGINEER','REVIEW_PROJECT_FORGE_PLAN_033'),
        ('SYSTEMS_ENGINEER','EDIT_PROJECT_FORGE_REVIEW_PLAN_033'),
        ('NETWORK_ENGINEER','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('NETWORK_ENGINEER','REVIEW_FLOWHIVE_PLANNER_066'),
        ('NETWORK_ENGINEER','EDIT_FLOWHIVE_PLANNER_066'),
        ('NETWORK_ENGINEER','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('NETWORK_ENGINEER','REVIEW_PROJECT_FORGE_PLAN_033'),
        ('NETWORK_ENGINEER','EDIT_PROJECT_FORGE_REVIEW_PLAN_033'),
        ('ENTERPRISE_NETWORK_ENGINEER','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('ENTERPRISE_NETWORK_ENGINEER','REVIEW_FLOWHIVE_PLANNER_066'),
        ('ENTERPRISE_NETWORK_ENGINEER','EDIT_FLOWHIVE_PLANNER_066'),
        ('ENTERPRISE_NETWORK_ENGINEER','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('ENTERPRISE_NETWORK_ENGINEER','REVIEW_PROJECT_FORGE_PLAN_033'),
        ('ENTERPRISE_NETWORK_ENGINEER','EDIT_PROJECT_FORGE_REVIEW_PLAN_033'),
        ('ACCOUNT_EXECUTIVE','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('ACCOUNT_EXECUTIVE','VIEW_PROJECT_FLOWHIVE_066'),
        ('ACCOUNT_EXECUTIVE','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('ACCOUNT_EXECUTIVE','VIEW_PROJECT_FORGE_033'),
        ('SALES_ACCOUNT_EXECUTIVE','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('SALES_ACCOUNT_EXECUTIVE','VIEW_PROJECT_FLOWHIVE_066'),
        ('SALES_ACCOUNT_EXECUTIVE','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('SALES_ACCOUNT_EXECUTIVE','VIEW_PROJECT_FORGE_033'),
        ('SOLUTION_ARCHITECT','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('SOLUTION_ARCHITECT','VIEW_PROJECT_FLOWHIVE_066'),
        ('SOLUTION_ARCHITECT','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('SOLUTION_ARCHITECT','VIEW_PROJECT_FORGE_033'),
        ('SOLUTIONS_ARCHITECT','VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066'),
        ('SOLUTIONS_ARCHITECT','VIEW_PROJECT_FLOWHIVE_066'),
        ('SOLUTIONS_ARCHITECT','VIEW_ASSOCIATED_PROJECT_FORGE_033'),
        ('SOLUTIONS_ARCHITECT','VIEW_PROJECT_FORGE_033')
), candidates AS (
    SELECT role.app_role_id,permission.app_permission_id
    FROM desired
    JOIN app_roles role
      ON UPPER(role.role_code)=desired.role_code
     AND role.is_active=TRUE
    JOIN app_permissions permission
      ON permission.permission_code=desired.permission_code
    LEFT JOIN app_role_permissions existing
      ON existing.app_role_id=role.app_role_id
     AND existing.app_permission_id=permission.app_permission_id
    WHERE existing.app_role_permission_id IS NULL
), inserted AS (
    INSERT INTO app_role_permissions(app_role_id,app_permission_id,created_at)
    SELECT app_role_id,app_permission_id,NOW() FROM candidates
    ON CONFLICT(app_role_id,app_permission_id) DO NOTHING
    RETURNING app_role_id,app_permission_id
)
INSERT INTO project_planning_095_role_grants(app_role_id,app_permission_id)
SELECT app_role_id,app_permission_id FROM inserted
ON CONFLICT DO NOTHING;

GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE project_planning_collaborators TO "ptp_app";
GRANT SELECT,INSERT ON TABLE project_planning_collaboration_audit_events TO "ptp_app";
GRANT SELECT ON TABLE project_planning_095_permissions_created,project_planning_095_role_grants TO "ptp_app";

INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES(
    '095_project_planning_collaboration_access',
    'Add project-scoped FlowHive and Project Forge planning collaboration for Engineering plus associated read-only AE/SA access without changing module ownership',
    NOW())
ON CONFLICT(migration_id) DO NOTHING;

COMMIT;
