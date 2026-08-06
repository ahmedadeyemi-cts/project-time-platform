-- ProjectPulse migration 074
-- Module 066 Project FlowHive production persistence, immutable version history,
-- reviewer-controlled baselines, RBAC, and Celar AI catalog rebranding.
--
-- FlowHive versions contain the validated planning contract and deterministic
-- schedule only. They never contain provider credentials, unrestricted private
-- document text, hidden prompts, or customer delivery tokens.

BEGIN;

DO $projectpulse074_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.projects') IS NULL
       OR to_regclass('public.project_tasks') IS NULL
       OR to_regclass('public.project_assignments') IS NULL
       OR to_regclass('public.app_users') IS NULL
       OR to_regclass('public.app_roles') IS NULL
       OR to_regclass('public.app_permissions') IS NULL
       OR to_regclass('public.app_role_permissions') IS NULL
       OR to_regclass('public.app_feature_catalog') IS NULL
       OR to_regclass('public.ai_capability_routes') IS NULL THEN
        RAISE EXCEPTION 'Migration 074 requires canonical project, identity, RBAC, and Module 064 routing foundations.';
    END IF;
END;
$projectpulse074_prerequisites$;

CREATE TABLE IF NOT EXISTS project_flowhive_plans (
    plan_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    plan_name VARCHAR(240) NOT NULL CHECK (length(btrim(plan_name)) >= 3),
    plan_status VARCHAR(32) NOT NULL DEFAULT 'draft' CHECK (plan_status IN (
        'draft', 'in_review', 'changes_requested', 'baselined', 'archived'
    )),
    current_version_number INTEGER NOT NULL DEFAULT 0 CHECK (current_version_number >= 0),
    baseline_version_number INTEGER NULL CHECK (baseline_version_number IS NULL OR baseline_version_number >= 1),
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    baselined_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    baselined_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    revision_number INTEGER NOT NULL DEFAULT 1 CHECK (revision_number >= 1),
    CONSTRAINT ck_project_flowhive_baseline_evidence CHECK (
        (baseline_version_number IS NULL AND baselined_by_user_id IS NULL AND baselined_at IS NULL)
        OR (baseline_version_number IS NOT NULL AND baselined_by_user_id IS NOT NULL AND baselined_at IS NOT NULL)
    )
);

CREATE INDEX IF NOT EXISTS ix_project_flowhive_plans_project_status
    ON project_flowhive_plans(project_id, plan_status, updated_at DESC);
CREATE INDEX IF NOT EXISTS ix_project_flowhive_plans_creator
    ON project_flowhive_plans(created_by_user_id, updated_at DESC);

CREATE TABLE IF NOT EXISTS project_flowhive_plan_versions (
    plan_version_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    plan_id UUID NOT NULL REFERENCES project_flowhive_plans(plan_id) ON DELETE CASCADE,
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    version_number INTEGER NOT NULL CHECK (version_number >= 1),
    revision_label VARCHAR(160) NOT NULL DEFAULT '',
    source_kind VARCHAR(32) NOT NULL DEFAULT 'manual' CHECK (source_kind IN (
        'manual', 'celar_ai', 'canonical_snapshot'
    )),
    plan_payload JSONB NOT NULL CHECK (jsonb_typeof(plan_payload) = 'object'),
    schedule_payload JSONB NOT NULL CHECK (jsonb_typeof(schedule_payload) = 'object'),
    validation_payload JSONB NOT NULL CHECK (jsonb_typeof(validation_payload) = 'object'),
    celar_ai_capability_code VARCHAR(120) NOT NULL DEFAULT 'project_flowhive_plan',
    celar_ai_provider_code VARCHAR(120) NOT NULL DEFAULT '',
    celar_ai_correlation_id VARCHAR(180) NOT NULL DEFAULT '',
    celar_ai_confidence NUMERIC(5,4) NULL CHECK (celar_ai_confidence IS NULL OR celar_ai_confidence BETWEEN 0 AND 1),
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_project_flowhive_plan_version UNIQUE (plan_id, version_number)
);

CREATE INDEX IF NOT EXISTS ix_project_flowhive_versions_project_time
    ON project_flowhive_plan_versions(project_id, created_at DESC);

CREATE TABLE IF NOT EXISTS project_flowhive_plan_reviews (
    review_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    plan_id UUID NOT NULL REFERENCES project_flowhive_plans(plan_id) ON DELETE RESTRICT,
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE RESTRICT,
    version_number INTEGER NOT NULL CHECK (version_number >= 1),
    decision VARCHAR(32) NOT NULL CHECK (decision IN ('approved_for_baseline', 'changes_requested')),
    review_note TEXT NOT NULL CHECK (length(btrim(review_note)) >= 10),
    actual_reviewer_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    effective_reviewer_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    reviewed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_project_flowhive_baseline_review UNIQUE (plan_id, version_number, decision)
);

CREATE INDEX IF NOT EXISTS ix_project_flowhive_reviews_project_time
    ON project_flowhive_plan_reviews(project_id, reviewed_at DESC);

CREATE TABLE IF NOT EXISTS project_flowhive_audit_events (
    audit_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL,
    plan_id UUID NOT NULL,
    version_number INTEGER NULL,
    event_code VARCHAR(100) NOT NULL,
    actual_actor_user_id UUID NOT NULL,
    effective_actor_user_id UUID NOT NULL,
    prior_state JSONB NULL,
    new_state JSONB NULL,
    event_metadata JSONB NOT NULL DEFAULT '{}'::JSONB CHECK (jsonb_typeof(event_metadata) = 'object'),
    correlation_id VARCHAR(180) NOT NULL DEFAULT '',
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_project_flowhive_audit_project_time
    ON project_flowhive_audit_events(project_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS ix_project_flowhive_audit_plan_time
    ON project_flowhive_audit_events(plan_id, occurred_at DESC);

CREATE OR REPLACE FUNCTION projectpulse074_touch_flowhive_plan()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse074_touch$
BEGIN
    NEW.updated_at := NOW();
    NEW.revision_number := OLD.revision_number + 1;
    RETURN NEW;
END;
$projectpulse074_touch$;

CREATE OR REPLACE FUNCTION projectpulse074_immutable_flowhive_evidence()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse074_immutable$
BEGIN
    RAISE EXCEPTION 'Project FlowHive version, review, and audit evidence is immutable.';
END;
$projectpulse074_immutable$;

DROP TRIGGER IF EXISTS trg_project_flowhive_plan_touch_074 ON project_flowhive_plans;
CREATE TRIGGER trg_project_flowhive_plan_touch_074
BEFORE UPDATE ON project_flowhive_plans
FOR EACH ROW EXECUTE FUNCTION projectpulse074_touch_flowhive_plan();

DROP TRIGGER IF EXISTS trg_project_flowhive_versions_immutable_074 ON project_flowhive_plan_versions;
CREATE TRIGGER trg_project_flowhive_versions_immutable_074
BEFORE UPDATE OR DELETE ON project_flowhive_plan_versions
FOR EACH ROW EXECUTE FUNCTION projectpulse074_immutable_flowhive_evidence();

DROP TRIGGER IF EXISTS trg_project_flowhive_reviews_immutable_074 ON project_flowhive_plan_reviews;
CREATE TRIGGER trg_project_flowhive_reviews_immutable_074
BEFORE UPDATE OR DELETE ON project_flowhive_plan_reviews
FOR EACH ROW EXECUTE FUNCTION projectpulse074_immutable_flowhive_evidence();

DROP TRIGGER IF EXISTS trg_project_flowhive_audit_immutable_074 ON project_flowhive_audit_events;
CREATE TRIGGER trg_project_flowhive_audit_immutable_074
BEFORE UPDATE OR DELETE ON project_flowhive_audit_events
FOR EACH ROW EXECUTE FUNCTION projectpulse074_immutable_flowhive_evidence();

CREATE TABLE IF NOT EXISTS project_flowhive_074_permissions_created (
    app_permission_id UUID PRIMARY KEY REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    permission_code VARCHAR(100) NOT NULL UNIQUE,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS project_flowhive_074_role_grants (
    app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE RESTRICT,
    app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (app_role_id, app_permission_id)
);

WITH inserted AS (
    INSERT INTO app_permissions(permission_code, permission_name, module_code, permission_description)
    VALUES
        ('VIEW_PROJECT_FLOWHIVE_066', 'View Project FlowHive', '066', 'View persisted FlowHive plans, immutable versions, schedules, and baseline evidence within authorized project scope.'),
        ('MANAGE_PROJECT_FLOWHIVE_066', 'Manage Project FlowHive', '066', 'Create and version validated FlowHive drafts within authorized project scope.'),
        ('BASELINE_PROJECT_FLOWHIVE_066', 'Approve Project FlowHive baseline', '066', 'Approve an exact immutable FlowHive plan version as the governed project baseline with a review note.')
    ON CONFLICT(permission_code) DO NOTHING
    RETURNING app_permission_id, permission_code
)
INSERT INTO project_flowhive_074_permissions_created(app_permission_id, permission_code)
SELECT app_permission_id, permission_code FROM inserted
ON CONFLICT DO NOTHING;

WITH desired(role_code, permission_code) AS (
    VALUES
        ('SUPER_ADMINISTRATOR', 'VIEW_PROJECT_FLOWHIVE_066'),
        ('SUPER_ADMINISTRATOR', 'MANAGE_PROJECT_FLOWHIVE_066'),
        ('SUPER_ADMINISTRATOR', 'BASELINE_PROJECT_FLOWHIVE_066'),
        ('SYSTEM_ADMINISTRATOR', 'VIEW_PROJECT_FLOWHIVE_066'),
        ('SYSTEM_ADMINISTRATOR', 'MANAGE_PROJECT_FLOWHIVE_066'),
        ('SYSTEM_ADMINISTRATOR', 'BASELINE_PROJECT_FLOWHIVE_066'),
        ('ADMINISTRATOR', 'VIEW_PROJECT_FLOWHIVE_066'),
        ('ADMINISTRATOR', 'MANAGE_PROJECT_FLOWHIVE_066'),
        ('ADMINISTRATOR', 'BASELINE_PROJECT_FLOWHIVE_066'),
        ('PROJECT_TEAM_COORDINATOR', 'VIEW_PROJECT_FLOWHIVE_066'),
        ('PROJECT_TEAM_COORDINATOR', 'MANAGE_PROJECT_FLOWHIVE_066'),
        ('PROJECT_TEAM_COORDINATOR', 'BASELINE_PROJECT_FLOWHIVE_066'),
        ('PROJECT_COORDINATOR', 'VIEW_PROJECT_FLOWHIVE_066'),
        ('PROJECT_COORDINATOR', 'MANAGE_PROJECT_FLOWHIVE_066'),
        ('PROJECT_COORDINATOR', 'BASELINE_PROJECT_FLOWHIVE_066'),
        ('PROJECT_MANAGER', 'VIEW_PROJECT_FLOWHIVE_066'),
        ('PROJECT_MANAGER', 'MANAGE_PROJECT_FLOWHIVE_066'),
        ('PROJECT_MANAGER', 'BASELINE_PROJECT_FLOWHIVE_066'),
        ('PROJECT_MANAGEMENT', 'VIEW_PROJECT_FLOWHIVE_066'),
        ('PROJECT_MANAGEMENT', 'MANAGE_PROJECT_FLOWHIVE_066'),
        ('PROJECT_MANAGEMENT', 'BASELINE_PROJECT_FLOWHIVE_066'),
        ('PROJECT_MANAGEMENT_LEAD', 'VIEW_PROJECT_FLOWHIVE_066'),
        ('PROJECT_MANAGEMENT_LEAD', 'MANAGE_PROJECT_FLOWHIVE_066'),
        ('PROJECT_MANAGEMENT_LEAD', 'BASELINE_PROJECT_FLOWHIVE_066'),
        ('PROJECT_MANAGEMENT_TEAM_LEAD', 'VIEW_PROJECT_FLOWHIVE_066'),
        ('PROJECT_MANAGEMENT_TEAM_LEAD', 'MANAGE_PROJECT_FLOWHIVE_066'),
        ('PROJECT_MANAGEMENT_TEAM_LEAD', 'BASELINE_PROJECT_FLOWHIVE_066'),
        ('PM_TEAM_LEAD', 'VIEW_PROJECT_FLOWHIVE_066'),
        ('PM_TEAM_LEAD', 'MANAGE_PROJECT_FLOWHIVE_066'),
        ('PM_TEAM_LEAD', 'BASELINE_PROJECT_FLOWHIVE_066'),
        ('MANAGER', 'VIEW_PROJECT_FLOWHIVE_066'),
        ('ENGINEERING_LEAD', 'VIEW_PROJECT_FLOWHIVE_066'),
        ('ENGINEERING_TEAM_LEAD', 'VIEW_PROJECT_FLOWHIVE_066'),
        ('ENGINEERING', 'VIEW_PROJECT_FLOWHIVE_066'),
        ('ENGINEER', 'VIEW_PROJECT_FLOWHIVE_066'),
        ('SYSTEMS_ENGINEER', 'VIEW_PROJECT_FLOWHIVE_066'),
        ('NETWORK_ENGINEER', 'VIEW_PROJECT_FLOWHIVE_066'),
        ('ENTERPRISE_NETWORK_ENGINEER', 'VIEW_PROJECT_FLOWHIVE_066')
), candidates AS (
    SELECT role.app_role_id, permission.app_permission_id
    FROM desired
    JOIN app_roles role ON UPPER(role.role_code)=desired.role_code AND role.is_active=TRUE
    JOIN app_permissions permission ON permission.permission_code=desired.permission_code
    LEFT JOIN app_role_permissions existing
      ON existing.app_role_id=role.app_role_id AND existing.app_permission_id=permission.app_permission_id
    WHERE existing.app_role_permission_id IS NULL
), inserted AS (
    INSERT INTO app_role_permissions(app_role_id, app_permission_id, created_at)
    SELECT app_role_id, app_permission_id, NOW() FROM candidates
    ON CONFLICT(app_role_id, app_permission_id) DO NOTHING
    RETURNING app_role_id, app_permission_id
)
INSERT INTO project_flowhive_074_role_grants(app_role_id, app_permission_id)
SELECT app_role_id, app_permission_id FROM inserted
ON CONFLICT DO NOTHING;

INSERT INTO app_feature_catalog(
    feature_code, feature_name, module_code, route_anchor,
    required_permission_code, feature_description, display_order, is_active)
VALUES(
    'PROJECT_FLOWHIVE_PRODUCTION',
    'Project FlowHive Production Planning',
    '066',
    '#project-flowhive',
    'VIEW_PROJECT_FLOWHIVE_066',
    'Persistent, versioned WBS, deterministic scheduling, Celar AI drafting, review evidence, and reviewer-controlled baselines.',
    166,
    TRUE)
ON CONFLICT(feature_code) DO UPDATE
SET feature_name=EXCLUDED.feature_name,
    module_code=EXCLUDED.module_code,
    route_anchor=EXCLUDED.route_anchor,
    required_permission_code=EXCLUDED.required_permission_code,
    feature_description=EXCLUDED.feature_description,
    is_active=TRUE,
    updated_at=NOW();

-- Preserve stable compatibility codes while removing the retired product name
-- from every catalog surface shown by administration and permissions pages.
UPDATE app_permissions
SET permission_name = replace(permission_name, 'Pulse AI', 'Celar AI'),
    permission_description = replace(permission_description, 'Pulse AI', 'Celar AI')
WHERE permission_name ILIKE '%Pulse AI%'
   OR permission_description ILIKE '%Pulse AI%';

UPDATE app_feature_catalog
SET feature_name = replace(feature_name, 'Pulse AI', 'Celar AI'),
    feature_description = replace(feature_description, 'Pulse AI', 'Celar AI'),
    updated_at = NOW()
WHERE feature_name ILIKE '%Pulse AI%'
   OR feature_description ILIKE '%Pulse AI%';

INSERT INTO schema_migrations(migration_id, description, applied_at)
VALUES(
    '074_module_066_project_flowhive_production',
    'Enable persistent versioned Project FlowHive planning, immutable reviews and baselines, scoped RBAC, and Celar AI catalog labels',
    NOW())
ON CONFLICT(migration_id) DO NOTHING;

COMMIT;
