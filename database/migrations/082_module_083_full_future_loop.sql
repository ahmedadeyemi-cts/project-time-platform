-- Pulse migration 082
-- Module 083 Full Future Loop: persistent, governed, sandbox-only lifecycle
-- testing from selective governance through private development, canary,
-- promotion, production evidence, support, repair, re-promotion, and closure.

BEGIN;

DO $pulse082_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.app_users') IS NULL
       OR to_regclass('public.app_roles') IS NULL
       OR to_regclass('public.app_permissions') IS NULL
       OR to_regclass('public.app_role_permissions') IS NULL
       OR to_regclass('public.app_feature_catalog') IS NULL THEN
        RAISE EXCEPTION 'Migration 082 requires canonical identity, RBAC, feature-catalog, and schema-migration foundations.';
    END IF;
END;
$pulse082_prerequisites$;

CREATE TABLE IF NOT EXISTS full_future_loop_items (
    loop_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    loop_number BIGSERIAL UNIQUE NOT NULL,
    title VARCHAR(200) NOT NULL CHECK (length(btrim(title)) >= 3),
    description TEXT NOT NULL DEFAULT '',
    change_type VARCHAR(40) NOT NULL DEFAULT 'major' CHECK (change_type IN (
        'standard','major','complex','architecture','security'
    )),
    selective_governance BOOLEAN NOT NULL DEFAULT TRUE,
    environment VARCHAR(24) NOT NULL DEFAULT 'sandbox' CHECK (environment='sandbox'),
    current_stage VARCHAR(48) NOT NULL CHECK (current_stage IN (
        'governance_pending','private_development','canary_ready','canary_failed',
        'promotion_ready','sandbox_production','production_signal','repair_open',
        'repair_canary_ready','repair_canary_failed','repromotion_ready',
        'sandbox_repromoted','verified_closed'
    )),
    current_status VARCHAR(32) NOT NULL DEFAULT 'active' CHECK (current_status IN (
        'active','attention_required','closed'
    )),
    source_repository VARCHAR(240) NOT NULL DEFAULT 'ahmedadeyemi-cts/project-time-platform',
    source_branch VARCHAR(240) NOT NULL DEFAULT 'sandbox/full-future-loop',
    source_commit VARCHAR(80) NOT NULL DEFAULT '',
    release_tag VARCHAR(160) NOT NULL DEFAULT '',
    last_canary_status VARCHAR(24) NOT NULL DEFAULT '' CHECK (last_canary_status IN ('','passed','failed')),
    iteration_number INTEGER NOT NULL DEFAULT 1 CHECK (iteration_number >= 1),
    revision_number INTEGER NOT NULL DEFAULT 1 CHECK (revision_number >= 1),
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    closed_at TIMESTAMPTZ NULL,
    CONSTRAINT ck_full_future_loop_closed CHECK (
        (current_stage='verified_closed' AND current_status='closed' AND closed_at IS NOT NULL)
        OR (current_stage<>'verified_closed' AND current_status<>'closed' AND closed_at IS NULL)
    )
);

CREATE INDEX IF NOT EXISTS ix_full_future_loop_items_stage
    ON full_future_loop_items(current_stage,current_status,updated_at DESC);
CREATE INDEX IF NOT EXISTS ix_full_future_loop_items_actor
    ON full_future_loop_items(created_by_user_id,updated_at DESC);

CREATE TABLE IF NOT EXISTS full_future_loop_events (
    event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    loop_id UUID NOT NULL REFERENCES full_future_loop_items(loop_id) ON DELETE RESTRICT,
    event_code VARCHAR(80) NOT NULL,
    from_stage VARCHAR(80) NULL,
    to_stage VARCHAR(80) NOT NULL,
    outcome VARCHAR(40) NOT NULL,
    summary TEXT NOT NULL,
    details JSONB NOT NULL DEFAULT '{}'::JSONB CHECK (jsonb_typeof(details)='object'),
    actual_actor_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    effective_actor_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_full_future_loop_events_loop
    ON full_future_loop_events(loop_id,occurred_at,event_id);
CREATE INDEX IF NOT EXISTS ix_full_future_loop_events_code
    ON full_future_loop_events(event_code,occurred_at DESC);

CREATE TABLE IF NOT EXISTS full_future_loop_artifacts (
    artifact_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    loop_id UUID NOT NULL REFERENCES full_future_loop_items(loop_id) ON DELETE RESTRICT,
    artifact_type VARCHAR(80) NOT NULL,
    artifact_code VARCHAR(100) NOT NULL,
    status VARCHAR(40) NOT NULL,
    title VARCHAR(240) NOT NULL,
    summary TEXT NOT NULL DEFAULT '',
    payload JSONB NOT NULL DEFAULT '{}'::JSONB CHECK (jsonb_typeof(payload)='object'),
    is_read_only BOOLEAN NOT NULL DEFAULT TRUE CHECK (is_read_only=TRUE),
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_full_future_loop_artifacts_loop
    ON full_future_loop_artifacts(loop_id,created_at,artifact_id);
CREATE INDEX IF NOT EXISTS ix_full_future_loop_artifacts_type
    ON full_future_loop_artifacts(artifact_type,status,created_at DESC);

CREATE OR REPLACE FUNCTION pulse082_touch_full_future_loop_item()
RETURNS TRIGGER LANGUAGE plpgsql AS $pulse082_touch$
BEGIN
    NEW.updated_at := NOW();
    NEW.revision_number := OLD.revision_number + 1;
    RETURN NEW;
END;
$pulse082_touch$;

CREATE OR REPLACE FUNCTION pulse082_immutable_full_future_loop_evidence()
RETURNS TRIGGER LANGUAGE plpgsql AS $pulse082_immutable$
BEGIN
    RAISE EXCEPTION 'Module 083 stage events and evidence artifacts are append-only and immutable.';
END;
$pulse082_immutable$;

DROP TRIGGER IF EXISTS trg_full_future_loop_item_touch_082 ON full_future_loop_items;
CREATE TRIGGER trg_full_future_loop_item_touch_082
BEFORE UPDATE ON full_future_loop_items
FOR EACH ROW EXECUTE FUNCTION pulse082_touch_full_future_loop_item();

DROP TRIGGER IF EXISTS trg_full_future_loop_events_immutable_082 ON full_future_loop_events;
CREATE TRIGGER trg_full_future_loop_events_immutable_082
BEFORE UPDATE OR DELETE ON full_future_loop_events
FOR EACH ROW EXECUTE FUNCTION pulse082_immutable_full_future_loop_evidence();

DROP TRIGGER IF EXISTS trg_full_future_loop_artifacts_immutable_082 ON full_future_loop_artifacts;
CREATE TRIGGER trg_full_future_loop_artifacts_immutable_082
BEFORE UPDATE OR DELETE ON full_future_loop_artifacts
FOR EACH ROW EXECUTE FUNCTION pulse082_immutable_full_future_loop_evidence();

CREATE TABLE IF NOT EXISTS full_future_loop_082_permissions_created (
    app_permission_id UUID PRIMARY KEY REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    permission_code VARCHAR(100) NOT NULL UNIQUE,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS full_future_loop_082_role_grants (
    app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE RESTRICT,
    app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY(app_role_id,app_permission_id)
);

WITH inserted AS (
    INSERT INTO app_permissions(permission_code,permission_name,module_code,permission_description)
    VALUES
      ('VIEW_FULL_FUTURE_LOOP_083','View Full Future Loop','083','View governed sandbox loops, stages, support guidance, and immutable evidence.'),
      ('RUN_FULL_FUTURE_LOOP_SANDBOX_083','Run Full Future Loop Sandbox','083','Create and execute safe sandbox lifecycle transitions without GitHub, deployment, cloud, secret, or production mutation.'),
      ('MANAGE_FULL_FUTURE_LOOP_083','Manage Full Future Loop','083','Reset sandbox iterations and administer Module 083 lifecycle testing.'),
      ('VIEW_FULL_FUTURE_LOOP_EVIDENCE_083','View Full Future Loop Evidence','083','View append-only decision, canary, production-signal, support, repair, promotion, and verification evidence.')
    ON CONFLICT(permission_code) DO NOTHING
    RETURNING app_permission_id,permission_code
)
INSERT INTO full_future_loop_082_permissions_created(app_permission_id,permission_code)
SELECT app_permission_id,permission_code FROM inserted ON CONFLICT DO NOTHING;

WITH desired(role_code,permission_code) AS (
    VALUES
      ('SUPER_ADMINISTRATOR','VIEW_FULL_FUTURE_LOOP_083'),
      ('SUPER_ADMINISTRATOR','RUN_FULL_FUTURE_LOOP_SANDBOX_083'),
      ('SUPER_ADMINISTRATOR','MANAGE_FULL_FUTURE_LOOP_083'),
      ('SUPER_ADMINISTRATOR','VIEW_FULL_FUTURE_LOOP_EVIDENCE_083'),
      ('ADMINISTRATOR','VIEW_FULL_FUTURE_LOOP_083'),
      ('ADMINISTRATOR','RUN_FULL_FUTURE_LOOP_SANDBOX_083'),
      ('ADMINISTRATOR','MANAGE_FULL_FUTURE_LOOP_083'),
      ('ADMINISTRATOR','VIEW_FULL_FUTURE_LOOP_EVIDENCE_083'),
      ('SYSTEM_ADMINISTRATOR','VIEW_FULL_FUTURE_LOOP_083'),
      ('SYSTEM_ADMINISTRATOR','RUN_FULL_FUTURE_LOOP_SANDBOX_083'),
      ('SYSTEM_ADMINISTRATOR','MANAGE_FULL_FUTURE_LOOP_083'),
      ('SYSTEM_ADMINISTRATOR','VIEW_FULL_FUTURE_LOOP_EVIDENCE_083'),
      ('PROJECT_TEAM_COORDINATOR','VIEW_FULL_FUTURE_LOOP_083'),
      ('PROJECT_TEAM_COORDINATOR','RUN_FULL_FUTURE_LOOP_SANDBOX_083'),
      ('PROJECT_TEAM_COORDINATOR','VIEW_FULL_FUTURE_LOOP_EVIDENCE_083'),
      ('MANAGER','VIEW_FULL_FUTURE_LOOP_083'),
      ('MANAGER','RUN_FULL_FUTURE_LOOP_SANDBOX_083'),
      ('MANAGER','VIEW_FULL_FUTURE_LOOP_EVIDENCE_083'),
      ('RELEASE_MANAGER','VIEW_FULL_FUTURE_LOOP_083'),
      ('RELEASE_MANAGER','RUN_FULL_FUTURE_LOOP_SANDBOX_083'),
      ('RELEASE_MANAGER','MANAGE_FULL_FUTURE_LOOP_083'),
      ('RELEASE_MANAGER','VIEW_FULL_FUTURE_LOOP_EVIDENCE_083'),
      ('ENGINEERING_MANAGER','VIEW_FULL_FUTURE_LOOP_083'),
      ('ENGINEERING_MANAGER','RUN_FULL_FUTURE_LOOP_SANDBOX_083'),
      ('ENGINEERING_MANAGER','VIEW_FULL_FUTURE_LOOP_EVIDENCE_083'),
      ('ENGINEERING_LEAD','VIEW_FULL_FUTURE_LOOP_083'),
      ('ENGINEERING_LEAD','RUN_FULL_FUTURE_LOOP_SANDBOX_083'),
      ('ENGINEERING_LEAD','VIEW_FULL_FUTURE_LOOP_EVIDENCE_083'),
      ('ENGINEERING_TEAM_LEAD','VIEW_FULL_FUTURE_LOOP_083'),
      ('ENGINEERING_TEAM_LEAD','RUN_FULL_FUTURE_LOOP_SANDBOX_083'),
      ('ENGINEERING_TEAM_LEAD','VIEW_FULL_FUTURE_LOOP_EVIDENCE_083'),
      ('PROJECT_MANAGER','VIEW_FULL_FUTURE_LOOP_083'),
      ('PROJECT_MANAGER','RUN_FULL_FUTURE_LOOP_SANDBOX_083'),
      ('PROJECT_MANAGER','VIEW_FULL_FUTURE_LOOP_EVIDENCE_083'),
      ('PROJECT_MANAGEMENT','VIEW_FULL_FUTURE_LOOP_083'),
      ('PROJECT_MANAGEMENT','RUN_FULL_FUTURE_LOOP_SANDBOX_083'),
      ('PROJECT_MANAGEMENT','VIEW_FULL_FUTURE_LOOP_EVIDENCE_083'),
      ('ENGINEER','VIEW_FULL_FUTURE_LOOP_083'),
      ('ENGINEER','VIEW_FULL_FUTURE_LOOP_EVIDENCE_083'),
      ('ENGINEERING','VIEW_FULL_FUTURE_LOOP_083'),
      ('ENGINEERING','VIEW_FULL_FUTURE_LOOP_EVIDENCE_083'),
      ('SOLUTION_ARCHITECT','VIEW_FULL_FUTURE_LOOP_083'),
      ('SOLUTION_ARCHITECT','VIEW_FULL_FUTURE_LOOP_EVIDENCE_083'),
      ('SUPPORT','VIEW_FULL_FUTURE_LOOP_083'),
      ('SUPPORT','VIEW_FULL_FUTURE_LOOP_EVIDENCE_083'),
      ('HELP_DESK','VIEW_FULL_FUTURE_LOOP_083'),
      ('HELP_DESK','VIEW_FULL_FUTURE_LOOP_EVIDENCE_083'),
      ('SERVICE_DESK','VIEW_FULL_FUTURE_LOOP_083'),
      ('SERVICE_DESK','VIEW_FULL_FUTURE_LOOP_EVIDENCE_083'),
      ('EXECUTIVE','VIEW_FULL_FUTURE_LOOP_083'),
      ('EXECUTIVE_LEADERSHIP','VIEW_FULL_FUTURE_LOOP_083')
), candidates AS (
    SELECT role.app_role_id,permission.app_permission_id
    FROM desired
    JOIN app_roles role ON upper(role.role_code)=desired.role_code AND role.is_active=TRUE
    JOIN app_permissions permission ON permission.permission_code=desired.permission_code
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
INSERT INTO full_future_loop_082_role_grants(app_role_id,app_permission_id)
SELECT app_role_id,app_permission_id FROM inserted ON CONFLICT DO NOTHING;

INSERT INTO app_feature_catalog(
    feature_code,feature_name,module_code,route_anchor,required_permission_code,
    feature_description,display_order,is_active)
VALUES(
    'FULL_FUTURE_LOOP_083','Full Future Loop','083','#full-future-loop',
    'VIEW_FULL_FUTURE_LOOP_083',
    'Safe persistent sandbox for selective governance, private development, canary validation, curated promotion, read-only production evidence, support, private repair, re-promotion, and final verification.',
    183,TRUE)
ON CONFLICT(feature_code) DO UPDATE SET
    feature_name=EXCLUDED.feature_name,
    module_code=EXCLUDED.module_code,
    route_anchor=EXCLUDED.route_anchor,
    required_permission_code=EXCLUDED.required_permission_code,
    feature_description=EXCLUDED.feature_description,
    is_active=TRUE,
    updated_at=NOW();

INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES(
    '082_module_083_full_future_loop',
    'Create Module 083 persistent sandbox loop, immutable events and artifacts, RBAC, and feature registration',
    NOW())
ON CONFLICT(migration_id) DO NOTHING;

COMMIT;
