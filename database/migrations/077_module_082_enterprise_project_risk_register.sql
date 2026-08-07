-- Pulse migration 077
-- Module 082 Enterprise Project Risk Register with PMI-aligned scoring,
-- project-scoped ownership, governed actions, and immutable versions/evidence.

BEGIN;

DO $pulse077_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.projects') IS NULL
       OR to_regclass('public.project_assignments') IS NULL
       OR to_regclass('public.app_users') IS NULL
       OR to_regclass('public.app_roles') IS NULL
       OR to_regclass('public.app_permissions') IS NULL
       OR to_regclass('public.app_role_permissions') IS NULL
       OR to_regclass('public.app_feature_catalog') IS NULL THEN
        RAISE EXCEPTION 'Migration 077 requires canonical project, assignment, identity, RBAC, and feature-catalog foundations.';
    END IF;
END;
$pulse077_prerequisites$;

CREATE TABLE IF NOT EXISTS project_risk_counters (
    project_id UUID PRIMARY KEY REFERENCES projects(project_id) ON DELETE CASCADE,
    last_risk_number INTEGER NOT NULL DEFAULT 0 CHECK (last_risk_number >= 0),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS project_risks (
    risk_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    risk_number INTEGER NOT NULL CHECK (risk_number >= 1),
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    project_code_snapshot VARCHAR(100) NOT NULL,
    project_name_snapshot VARCHAR(255) NOT NULL,
    customer_name_snapshot VARCHAR(255) NOT NULL DEFAULT '',
    risk_title VARCHAR(240) NOT NULL CHECK (length(btrim(risk_title)) >= 3),
    cause_statement TEXT NOT NULL,
    uncertain_event_statement TEXT NOT NULL,
    impact_statement TEXT NOT NULL,
    description TEXT NOT NULL DEFAULT '',
    risk_type VARCHAR(16) NOT NULL CHECK (risk_type IN ('threat','opportunity')),
    category VARCHAR(100) NOT NULL,
    subcategory VARCHAR(120) NOT NULL DEFAULT '',
    date_identified DATE NOT NULL DEFAULT CURRENT_DATE,
    identified_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    risk_owner_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    probability_score SMALLINT NOT NULL CHECK (probability_score BETWEEN 1 AND 5),
    schedule_impact_score SMALLINT NOT NULL DEFAULT 1 CHECK (schedule_impact_score BETWEEN 1 AND 5),
    cost_impact_score SMALLINT NOT NULL DEFAULT 1 CHECK (cost_impact_score BETWEEN 1 AND 5),
    scope_impact_score SMALLINT NOT NULL DEFAULT 1 CHECK (scope_impact_score BETWEEN 1 AND 5),
    quality_impact_score SMALLINT NOT NULL DEFAULT 1 CHECK (quality_impact_score BETWEEN 1 AND 5),
    customer_impact_score SMALLINT NOT NULL DEFAULT 1 CHECK (customer_impact_score BETWEEN 1 AND 5),
    security_impact_score SMALLINT NOT NULL DEFAULT 1 CHECK (security_impact_score BETWEEN 1 AND 5),
    compliance_impact_score SMALLINT NOT NULL DEFAULT 1 CHECK (compliance_impact_score BETWEEN 1 AND 5),
    resource_impact_score SMALLINT NOT NULL DEFAULT 1 CHECK (resource_impact_score BETWEEN 1 AND 5),
    operational_impact_score SMALLINT NOT NULL DEFAULT 1 CHECK (operational_impact_score BETWEEN 1 AND 5),
    overall_impact_score SMALLINT GENERATED ALWAYS AS (GREATEST(
        schedule_impact_score,cost_impact_score,scope_impact_score,quality_impact_score,
        customer_impact_score,security_impact_score,compliance_impact_score,
        resource_impact_score,operational_impact_score
    )) STORED,
    inherent_exposure SMALLINT GENERATED ALWAYS AS (probability_score * GREATEST(
        schedule_impact_score,cost_impact_score,scope_impact_score,quality_impact_score,
        customer_impact_score,security_impact_score,compliance_impact_score,
        resource_impact_score,operational_impact_score
    )) STORED,
    proximity VARCHAR(80) NOT NULL DEFAULT '',
    velocity VARCHAR(24) NOT NULL DEFAULT 'normal' CHECK (velocity IN ('low','normal','high','immediate')),
    response_strategy VARCHAR(40) NOT NULL CHECK (response_strategy IN (
        'avoid','mitigate','transfer','accept','escalate','exploit','enhance','share'
    )),
    response_plan TEXT NOT NULL DEFAULT '',
    mitigation_actions TEXT NOT NULL DEFAULT '',
    contingency_plan TEXT NOT NULL DEFAULT '',
    trigger_indicator TEXT NOT NULL DEFAULT '',
    response_cost NUMERIC(14,2) NULL CHECK (response_cost IS NULL OR response_cost >= 0),
    response_schedule_impact_days INTEGER NULL,
    target_response_date DATE NULL,
    next_review_date DATE NOT NULL,
    review_cadence VARCHAR(24) NOT NULL DEFAULT 'monthly' CHECK (review_cadence IN (
        'weekly','biweekly','monthly','quarterly','event_driven'
    )),
    risk_status VARCHAR(32) NOT NULL DEFAULT 'proposed' CHECK (risk_status IN (
        'proposed','open','monitoring','response_in_progress','accepted','realized','closed','retired'
    )),
    residual_probability_score SMALLINT NULL CHECK (residual_probability_score IS NULL OR residual_probability_score BETWEEN 1 AND 5),
    residual_impact_score SMALLINT NULL CHECK (residual_impact_score IS NULL OR residual_impact_score BETWEEN 1 AND 5),
    residual_exposure SMALLINT GENERATED ALWAYS AS (
        CASE WHEN residual_probability_score IS NULL OR residual_impact_score IS NULL
             THEN NULL ELSE residual_probability_score * residual_impact_score END
    ) STORED,
    escalation_level VARCHAR(24) NOT NULL DEFAULT 'project' CHECK (escalation_level IN (
        'project','pmo','executive','security','compliance','customer'
    )),
    escalation_decision TEXT NOT NULL DEFAULT '',
    issue_reference VARCHAR(180) NOT NULL DEFAULT '',
    realized_at TIMESTAMPTZ NULL,
    assumptions TEXT NOT NULL DEFAULT '',
    dependencies TEXT NOT NULL DEFAULT '',
    evidence_references JSONB NOT NULL DEFAULT '[]'::JSONB CHECK (jsonb_typeof(evidence_references)='array'),
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    closed_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    closed_at TIMESTAMPTZ NULL,
    revision_number INTEGER NOT NULL DEFAULT 1 CHECK (revision_number >= 1),
    UNIQUE(project_id,risk_number),
    CONSTRAINT ck_project_risk_strategy CHECK (
        (risk_type='threat' AND response_strategy IN ('avoid','mitigate','transfer','accept','escalate'))
        OR (risk_type='opportunity' AND response_strategy IN ('exploit','enhance','share','accept','escalate'))
    ),
    CONSTRAINT ck_project_risk_response_date CHECK (target_response_date IS NULL OR target_response_date>=date_identified),
    CONSTRAINT ck_project_risk_realized CHECK ((risk_status='realized' AND realized_at IS NOT NULL) OR (risk_status<>'realized')),
    CONSTRAINT ck_project_risk_closed CHECK (
        (risk_status IN ('closed','retired') AND closed_at IS NOT NULL AND closed_by_user_id IS NOT NULL)
        OR (risk_status NOT IN ('closed','retired') AND closed_at IS NULL AND closed_by_user_id IS NULL)
    )
);

CREATE INDEX IF NOT EXISTS ix_project_risks_project_status
    ON project_risks(project_id,risk_status,next_review_date);
CREATE INDEX IF NOT EXISTS ix_project_risks_owner_status
    ON project_risks(risk_owner_user_id,risk_status,next_review_date);
CREATE INDEX IF NOT EXISTS ix_project_risks_exposure
    ON project_risks(inherent_exposure DESC,residual_exposure DESC NULLS LAST)
    WHERE risk_status NOT IN ('closed','retired');

CREATE TABLE IF NOT EXISTS project_risk_versions (
    risk_version_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    risk_id UUID NOT NULL REFERENCES project_risks(risk_id) ON DELETE RESTRICT,
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE RESTRICT,
    version_number INTEGER NOT NULL CHECK (version_number >= 1),
    risk_snapshot JSONB NOT NULL CHECK (jsonb_typeof(risk_snapshot)='object'),
    change_reason TEXT NOT NULL,
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(risk_id,version_number)
);

CREATE INDEX IF NOT EXISTS ix_project_risk_versions_project
    ON project_risk_versions(project_id,created_at DESC);

CREATE TABLE IF NOT EXISTS project_risk_actions (
    risk_action_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    risk_id UUID NOT NULL REFERENCES project_risks(risk_id) ON DELETE CASCADE,
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    action_title VARCHAR(240) NOT NULL CHECK (length(btrim(action_title)) >= 3),
    action_description TEXT NOT NULL DEFAULT '',
    owner_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    due_date DATE NOT NULL,
    action_status VARCHAR(24) NOT NULL DEFAULT 'not_started' CHECK (action_status IN (
        'not_started','in_progress','blocked','completed','cancelled'
    )),
    completion_evidence TEXT NOT NULL DEFAULT '',
    notes TEXT NOT NULL DEFAULT '',
    completed_at TIMESTAMPTZ NULL,
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    revision_number INTEGER NOT NULL DEFAULT 1 CHECK (revision_number >= 1),
    CONSTRAINT ck_project_risk_action_completion CHECK (
        (action_status='completed' AND completed_at IS NOT NULL AND btrim(completion_evidence)<>'')
        OR (action_status<>'completed' AND completed_at IS NULL)
    )
);

CREATE INDEX IF NOT EXISTS ix_project_risk_actions_attention
    ON project_risk_actions(owner_user_id,action_status,due_date);
CREATE INDEX IF NOT EXISTS ix_project_risk_actions_project
    ON project_risk_actions(project_id,risk_id,due_date);

CREATE TABLE IF NOT EXISTS project_risk_action_history (
    action_history_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    risk_action_id UUID NOT NULL REFERENCES project_risk_actions(risk_action_id) ON DELETE RESTRICT,
    risk_id UUID NOT NULL REFERENCES project_risks(risk_id) ON DELETE RESTRICT,
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE RESTRICT,
    version_number INTEGER NOT NULL CHECK (version_number >= 1),
    action_snapshot JSONB NOT NULL CHECK (jsonb_typeof(action_snapshot)='object'),
    change_reason TEXT NOT NULL,
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(risk_action_id,version_number)
);

CREATE TABLE IF NOT EXISTS project_risk_audit_events (
    audit_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NULL,
    risk_id UUID NULL,
    risk_action_id UUID NULL,
    event_code VARCHAR(80) NOT NULL,
    actual_actor_user_id UUID NOT NULL,
    effective_actor_user_id UUID NOT NULL,
    prior_state JSONB NULL,
    new_state JSONB NULL,
    event_metadata JSONB NOT NULL DEFAULT '{}'::JSONB CHECK (jsonb_typeof(event_metadata)='object'),
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_project_risk_audit_project
    ON project_risk_audit_events(project_id,occurred_at DESC);
CREATE INDEX IF NOT EXISTS ix_project_risk_audit_risk
    ON project_risk_audit_events(risk_id,occurred_at DESC);

CREATE OR REPLACE FUNCTION pulse077_next_risk_number()
RETURNS TRIGGER LANGUAGE plpgsql AS $pulse077_number$
DECLARE assigned_number INTEGER;
BEGIN
    INSERT INTO project_risk_counters(project_id,last_risk_number)
    VALUES(NEW.project_id,1)
    ON CONFLICT(project_id) DO UPDATE SET
      last_risk_number=project_risk_counters.last_risk_number+1,
      updated_at=NOW()
    RETURNING last_risk_number INTO assigned_number;
    NEW.risk_number:=assigned_number;
    RETURN NEW;
END;
$pulse077_number$;

CREATE OR REPLACE FUNCTION pulse077_touch_revision()
RETURNS TRIGGER LANGUAGE plpgsql AS $pulse077_touch$
BEGIN
    IF OLD.risk_status IN ('closed','retired') THEN
        RAISE EXCEPTION 'Closed and retired risks are immutable.';
    END IF;
    NEW.updated_at:=NOW();
    NEW.revision_number:=OLD.revision_number+1;
    RETURN NEW;
END;
$pulse077_touch$;

CREATE OR REPLACE FUNCTION pulse077_touch_action()
RETURNS TRIGGER LANGUAGE plpgsql AS $pulse077_action$
BEGIN
    IF EXISTS(SELECT 1 FROM project_risks risk WHERE risk.risk_id=NEW.risk_id AND risk.risk_status IN ('closed','retired')) THEN
        RAISE EXCEPTION 'Actions for closed and retired risks are immutable.';
    END IF;
    NEW.updated_at:=NOW();
    NEW.revision_number:=OLD.revision_number+1;
    RETURN NEW;
END;
$pulse077_action$;

CREATE OR REPLACE FUNCTION pulse077_validate_owner()
RETURNS TRIGGER LANGUAGE plpgsql AS $pulse077_owner$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM app_users owner WHERE owner.user_id=NEW.risk_owner_user_id AND owner.is_active=TRUE) THEN
        RAISE EXCEPTION 'Risk owner must be an active user.';
    END IF;
    RETURN NEW;
END;
$pulse077_owner$;

CREATE OR REPLACE FUNCTION pulse077_validate_action()
RETURNS TRIGGER LANGUAGE plpgsql AS $pulse077_validate_action$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM app_users owner WHERE owner.user_id=NEW.owner_user_id AND owner.is_active=TRUE) THEN
        RAISE EXCEPTION 'Action owner must be an active user.';
    END IF;
    IF NOT EXISTS(SELECT 1 FROM project_risks risk WHERE risk.risk_id=NEW.risk_id AND risk.project_id=NEW.project_id) THEN
        RAISE EXCEPTION 'Risk action project does not match its risk.';
    END IF;
    RETURN NEW;
END;
$pulse077_validate_action$;

CREATE OR REPLACE FUNCTION pulse077_immutable_evidence()
RETURNS TRIGGER LANGUAGE plpgsql AS $pulse077_immutable$
BEGIN
    RAISE EXCEPTION 'Module 082 version, action history, and audit evidence is immutable.';
END;
$pulse077_immutable$;

DROP TRIGGER IF EXISTS trg_project_risk_number_077 ON project_risks;
CREATE TRIGGER trg_project_risk_number_077 BEFORE INSERT ON project_risks
FOR EACH ROW EXECUTE FUNCTION pulse077_next_risk_number();
DROP TRIGGER IF EXISTS trg_project_risk_owner_077 ON project_risks;
CREATE TRIGGER trg_project_risk_owner_077 BEFORE INSERT OR UPDATE ON project_risks
FOR EACH ROW EXECUTE FUNCTION pulse077_validate_owner();
DROP TRIGGER IF EXISTS trg_project_risk_touch_077 ON project_risks;
CREATE TRIGGER trg_project_risk_touch_077 BEFORE UPDATE ON project_risks
FOR EACH ROW EXECUTE FUNCTION pulse077_touch_revision();
DROP TRIGGER IF EXISTS trg_project_risk_action_validate_077 ON project_risk_actions;
CREATE TRIGGER trg_project_risk_action_validate_077 BEFORE INSERT OR UPDATE ON project_risk_actions
FOR EACH ROW EXECUTE FUNCTION pulse077_validate_action();
DROP TRIGGER IF EXISTS trg_project_risk_action_touch_077 ON project_risk_actions;
CREATE TRIGGER trg_project_risk_action_touch_077 BEFORE UPDATE ON project_risk_actions
FOR EACH ROW EXECUTE FUNCTION pulse077_touch_action();
DROP TRIGGER IF EXISTS trg_project_risk_versions_immutable_077 ON project_risk_versions;
CREATE TRIGGER trg_project_risk_versions_immutable_077 BEFORE UPDATE OR DELETE ON project_risk_versions
FOR EACH ROW EXECUTE FUNCTION pulse077_immutable_evidence();
DROP TRIGGER IF EXISTS trg_project_risk_action_history_immutable_077 ON project_risk_action_history;
CREATE TRIGGER trg_project_risk_action_history_immutable_077 BEFORE UPDATE OR DELETE ON project_risk_action_history
FOR EACH ROW EXECUTE FUNCTION pulse077_immutable_evidence();
DROP TRIGGER IF EXISTS trg_project_risk_audit_immutable_077 ON project_risk_audit_events;
CREATE TRIGGER trg_project_risk_audit_immutable_077 BEFORE UPDATE OR DELETE ON project_risk_audit_events
FOR EACH ROW EXECUTE FUNCTION pulse077_immutable_evidence();

CREATE TABLE IF NOT EXISTS project_risk_077_permissions_created(
    app_permission_id UUID PRIMARY KEY REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    permission_code VARCHAR(100) NOT NULL UNIQUE,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE IF NOT EXISTS project_risk_077_role_grants(
    app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE RESTRICT,
    app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY(app_role_id,app_permission_id)
);

WITH inserted AS (
    INSERT INTO app_permissions(permission_code,permission_name,module_code,permission_description)
    VALUES
      ('VIEW_PROJECT_RISKS_082','View Enterprise Project Risks','082','View risks, heatmaps, actions, reviews, portfolio summaries, and history within authoritative project scope.'),
      ('MANAGE_PROJECT_RISKS_082','Manage Enterprise Project Risks','082','Create, reassess, respond to, realize, close, and govern risks within authoritative project scope.'),
      ('UPDATE_ASSIGNED_RISK_ACTIONS_082','Update Assigned Risk Actions','082','Update actions assigned to the current active user on an authorized project.'),
      ('EXPORT_PROJECT_RISKS_082','Export Enterprise Project Risks','082','Create US Signal-branded role-scoped Excel and PDF risk evidence artifacts.')
    ON CONFLICT(permission_code) DO NOTHING
    RETURNING app_permission_id,permission_code
)
INSERT INTO project_risk_077_permissions_created(app_permission_id,permission_code)
SELECT app_permission_id,permission_code FROM inserted ON CONFLICT DO NOTHING;

WITH desired(role_code,permission_code) AS (
    VALUES
      ('SUPER_ADMINISTRATOR','VIEW_PROJECT_RISKS_082'),('SUPER_ADMINISTRATOR','MANAGE_PROJECT_RISKS_082'),('SUPER_ADMINISTRATOR','EXPORT_PROJECT_RISKS_082'),
      ('ADMINISTRATOR','VIEW_PROJECT_RISKS_082'),('ADMINISTRATOR','MANAGE_PROJECT_RISKS_082'),('ADMINISTRATOR','EXPORT_PROJECT_RISKS_082'),
      ('PROJECT_TEAM_COORDINATOR','VIEW_PROJECT_RISKS_082'),('PROJECT_TEAM_COORDINATOR','MANAGE_PROJECT_RISKS_082'),('PROJECT_TEAM_COORDINATOR','EXPORT_PROJECT_RISKS_082'),
      ('PROJECT_MANAGER','VIEW_PROJECT_RISKS_082'),('PROJECT_MANAGER','MANAGE_PROJECT_RISKS_082'),('PROJECT_MANAGER','EXPORT_PROJECT_RISKS_082'),
      ('PROJECT_MANAGEMENT','VIEW_PROJECT_RISKS_082'),('PROJECT_MANAGEMENT','MANAGE_PROJECT_RISKS_082'),('PROJECT_MANAGEMENT','EXPORT_PROJECT_RISKS_082'),
      ('PROJECT_MANAGEMENT_LEAD','VIEW_PROJECT_RISKS_082'),('PROJECT_MANAGEMENT_LEAD','MANAGE_PROJECT_RISKS_082'),('PROJECT_MANAGEMENT_LEAD','EXPORT_PROJECT_RISKS_082'),
      ('PROJECT_MANAGEMENT_TEAM_LEAD','VIEW_PROJECT_RISKS_082'),('PROJECT_MANAGEMENT_TEAM_LEAD','MANAGE_PROJECT_RISKS_082'),('PROJECT_MANAGEMENT_TEAM_LEAD','EXPORT_PROJECT_RISKS_082'),
      ('PM_TEAM_LEAD','VIEW_PROJECT_RISKS_082'),('PM_TEAM_LEAD','MANAGE_PROJECT_RISKS_082'),('PM_TEAM_LEAD','EXPORT_PROJECT_RISKS_082'),
      ('MANAGER','VIEW_PROJECT_RISKS_082'),('MANAGER','MANAGE_PROJECT_RISKS_082'),('MANAGER','EXPORT_PROJECT_RISKS_082'),
      ('ENGINEERING_TEAM_LEAD','VIEW_PROJECT_RISKS_082'),('ENGINEERING_TEAM_LEAD','MANAGE_PROJECT_RISKS_082'),
      ('ENGINEER','VIEW_PROJECT_RISKS_082'),('ENGINEER','UPDATE_ASSIGNED_RISK_ACTIONS_082'),
      ('ENGINEERING','VIEW_PROJECT_RISKS_082'),('ENGINEERING','UPDATE_ASSIGNED_RISK_ACTIONS_082'),
      ('SOLUTION_ARCHITECT','VIEW_PROJECT_RISKS_082'),('SOLUTION_ARCHITECT','UPDATE_ASSIGNED_RISK_ACTIONS_082'),
      ('ACCOUNT_EXECUTIVE','VIEW_PROJECT_RISKS_082'),('EXECUTIVE','VIEW_PROJECT_RISKS_082'),
      ('ACCOUNTING','VIEW_PROJECT_RISKS_082'),('SALES','VIEW_PROJECT_RISKS_082')
), candidates AS (
    SELECT role.app_role_id,permission.app_permission_id
    FROM desired
    JOIN app_roles role ON upper(role.role_code)=desired.role_code AND role.is_active=TRUE
    JOIN app_permissions permission ON permission.permission_code=desired.permission_code
    LEFT JOIN app_role_permissions existing ON existing.app_role_id=role.app_role_id AND existing.app_permission_id=permission.app_permission_id
    WHERE existing.app_role_permission_id IS NULL
), inserted AS (
    INSERT INTO app_role_permissions(app_role_id,app_permission_id,created_at)
    SELECT app_role_id,app_permission_id,NOW() FROM candidates
    ON CONFLICT(app_role_id,app_permission_id) DO NOTHING
    RETURNING app_role_id,app_permission_id
)
INSERT INTO project_risk_077_role_grants(app_role_id,app_permission_id)
SELECT app_role_id,app_permission_id FROM inserted ON CONFLICT DO NOTHING;

INSERT INTO app_feature_catalog(feature_code,feature_name,module_code,route_anchor,required_permission_code,feature_description,display_order,is_active)
VALUES('ENTERPRISE_PROJECT_RISK_REGISTER_082','Enterprise Project Risk Register','082','#project-risk-register','VIEW_PROJECT_RISKS_082','PMI-aligned project risks, inherent and residual heatmaps, governed response actions, overdue reviews, immutable history, and branded exports.',182,TRUE)
ON CONFLICT(feature_code) DO UPDATE SET
  feature_name=EXCLUDED.feature_name,module_code=EXCLUDED.module_code,route_anchor=EXCLUDED.route_anchor,
  required_permission_code=EXCLUDED.required_permission_code,feature_description=EXCLUDED.feature_description,
  is_active=TRUE,updated_at=NOW();

INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES('077_module_082_enterprise_project_risk_register','Create scoped Module 082 PMI-aligned risks, actions, immutable versions and audit, RBAC, and export foundations',NOW())
ON CONFLICT(migration_id) DO NOTHING;

COMMIT;
