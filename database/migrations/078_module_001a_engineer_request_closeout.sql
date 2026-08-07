-- Pulse migration 078
-- Module 001A Engineer Request Closeout
--
-- Engineers may close only their own assigned Service Request, Pre-Sales, and
-- Internal tasks. The closeout immediately removes the assignment from Module
-- 001 billing choices, preserves prior time, blocks new/increased time, and
-- creates immutable workflow evidence. Module 055C remains the only final
-- request-close authority for Project Team Coordinators.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

DO $projectpulse078_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.projects') IS NULL
       OR to_regclass('public.project_tasks') IS NULL
       OR to_regclass('public.project_assignments') IS NULL
       OR to_regclass('public.time_entries') IS NULL
       OR to_regclass('public.app_users') IS NULL
       OR to_regclass('public.app_permissions') IS NULL
       OR to_regclass('public.app_roles') IS NULL
       OR to_regclass('public.app_role_permissions') IS NULL
       OR to_regclass('public.project_notification_dispatches') IS NULL
       OR to_regclass('public.project_notification_dispatch_recipients') IS NULL THEN
        RAISE EXCEPTION 'Migration 078 requires the Module 001, Module 055C, RBAC, and Module 065 notification foundations.';
    END IF;
END;
$projectpulse078_prerequisites$;

ALTER TABLE project_assignments
    ADD COLUMN IF NOT EXISTS module001a_closeout_status VARCHAR(32) NOT NULL DEFAULT 'active',
    ADD COLUMN IF NOT EXISTS module001a_closeout_updated_at TIMESTAMPTZ NULL;

ALTER TABLE project_assignments
    DROP CONSTRAINT IF EXISTS chk_project_assignments_module001a_closeout_status;
ALTER TABLE project_assignments
    ADD CONSTRAINT chk_project_assignments_module001a_closeout_status
    CHECK (module001a_closeout_status IN ('active', 'engineer_closed', 'ptc_final_closed'));

CREATE TABLE IF NOT EXISTS module001a_engineer_task_closeouts (
    module001a_closeout_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    assignment_id UUID NOT NULL UNIQUE REFERENCES project_assignments(project_assignment_id) ON DELETE RESTRICT,
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE RESTRICT,
    task_id UUID NOT NULL REFERENCES project_tasks(task_id) ON DELETE RESTRICT,
    engineer_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    closeout_status VARCHAR(32) NOT NULL DEFAULT 'engineer_closed'
        CHECK (closeout_status IN ('engineer_closed', 'reopened', 'ptc_final_closed')),
    completion_summary TEXT NOT NULL DEFAULT '',
    engineer_closed_at TIMESTAMPTZ NOT NULL,
    engineer_closed_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    reopened_at TIMESTAMPTZ NULL,
    reopened_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    reopen_reason TEXT NOT NULL DEFAULT '',
    ptc_final_closed_at TIMESTAMPTZ NULL,
    revision_number INTEGER NOT NULL DEFAULT 1 CHECK (revision_number > 0),
    latest_notification_dispatch_id UUID NULL REFERENCES project_notification_dispatches(project_notification_dispatch_id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_module001a_closeouts_engineer_status
    ON module001a_engineer_task_closeouts(engineer_user_id, closeout_status, updated_at DESC);
CREATE INDEX IF NOT EXISTS idx_module001a_closeouts_project_status
    ON module001a_engineer_task_closeouts(project_id, closeout_status, updated_at DESC);

CREATE TABLE IF NOT EXISTS module001a_engineer_task_closeout_events (
    module001a_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    module001a_closeout_id UUID NOT NULL REFERENCES module001a_engineer_task_closeouts(module001a_closeout_id) ON DELETE RESTRICT,
    assignment_id UUID NOT NULL REFERENCES project_assignments(project_assignment_id) ON DELETE RESTRICT,
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE RESTRICT,
    task_id UUID NOT NULL REFERENCES project_tasks(task_id) ON DELETE RESTRICT,
    engineer_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    event_type VARCHAR(40) NOT NULL CHECK (event_type IN ('engineer_closed', 'engineer_reopened', 'ptc_final_closed')),
    event_reason TEXT NOT NULL DEFAULT '',
    actor_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    notification_dispatch_id UUID NULL REFERENCES project_notification_dispatches(project_notification_dispatch_id) ON DELETE SET NULL,
    evidence_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_module001a_events_engineer_occurred
    ON module001a_engineer_task_closeout_events(engineer_user_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_module001a_events_closeout_occurred
    ON module001a_engineer_task_closeout_events(module001a_closeout_id, occurred_at DESC);

CREATE OR REPLACE FUNCTION projectpulse078_touch_closeout()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse078_touch$
BEGIN
    NEW.updated_at := NOW();
    IF ROW(NEW.closeout_status, NEW.completion_summary, NEW.reopen_reason, NEW.ptc_final_closed_at)
       IS DISTINCT FROM
       ROW(OLD.closeout_status, OLD.completion_summary, OLD.reopen_reason, OLD.ptc_final_closed_at) THEN
        NEW.revision_number := OLD.revision_number + 1;
    END IF;
    RETURN NEW;
END;
$projectpulse078_touch$;

CREATE OR REPLACE FUNCTION projectpulse078_immutable_closeout_event()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse078_event_immutable$
BEGIN
    RAISE EXCEPTION 'Module 001A closeout transition evidence is immutable.';
END;
$projectpulse078_event_immutable$;

CREATE OR REPLACE FUNCTION projectpulse078_block_closed_assignment_time()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse078_time_guard$
DECLARE
    should_check BOOLEAN;
BEGIN
    IF TG_OP = 'INSERT' THEN
        should_check := TRUE;
    ELSE
        should_check := NEW.user_id IS DISTINCT FROM OLD.user_id
            OR NEW.project_id IS DISTINCT FROM OLD.project_id
            OR NEW.task_id IS DISTINCT FROM OLD.task_id
            OR NEW.hours > OLD.hours;
    END IF;

    IF NOT should_check OR NEW.project_id IS NULL OR NEW.task_id IS NULL THEN
        RETURN NEW;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM module001a_engineer_task_closeouts closeout
        WHERE closeout.engineer_user_id = NEW.user_id
          AND closeout.project_id = NEW.project_id
          AND closeout.task_id = NEW.task_id
          AND closeout.closeout_status IN ('engineer_closed', 'ptc_final_closed')
    ) THEN
        RAISE EXCEPTION 'Billing is locked for this Engineer task because it is closed in Module 001A.';
    END IF;

    RETURN NEW;
END;
$projectpulse078_time_guard$;

CREATE OR REPLACE FUNCTION projectpulse078_finalize_project_closeouts()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse078_project_final$
BEGIN
    IF lower(coalesce(NEW.status, '')) IN ('closed', 'complete', 'completed', 'done', 'cancelled', 'canceled', 'archived')
       AND lower(coalesce(OLD.status, '')) NOT IN ('closed', 'complete', 'completed', 'done', 'cancelled', 'canceled', 'archived') THEN
        UPDATE module001a_engineer_task_closeouts
        SET closeout_status = 'ptc_final_closed',
            ptc_final_closed_at = COALESCE(ptc_final_closed_at, NOW())
        WHERE project_id = NEW.project_id
          AND closeout_status <> 'ptc_final_closed';

        UPDATE project_assignments
        SET module001a_closeout_status = 'ptc_final_closed',
            module001a_closeout_updated_at = NOW()
        WHERE project_id = NEW.project_id
          AND module001a_closeout_status <> 'ptc_final_closed';

        INSERT INTO module001a_engineer_task_closeout_events (
            module001a_closeout_id, assignment_id, project_id, task_id,
            engineer_user_id, event_type, event_reason, evidence_json, occurred_at)
        SELECT closeout.module001a_closeout_id, closeout.assignment_id,
               closeout.project_id, closeout.task_id, closeout.engineer_user_id,
               'ptc_final_closed',
               'The original request was closed by the Module 055C lifecycle authority.',
               jsonb_build_object('projectStatus', NEW.status, 'sourceModule', '055C'),
               NOW()
        FROM module001a_engineer_task_closeouts closeout
        WHERE closeout.project_id = NEW.project_id
          AND closeout.ptc_final_closed_at IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM module001a_engineer_task_closeout_events event
              WHERE event.module001a_closeout_id = closeout.module001a_closeout_id
                AND event.event_type = 'ptc_final_closed'
          );
    END IF;
    RETURN NEW;
END;
$projectpulse078_project_final$;

CREATE OR REPLACE FUNCTION projectpulse078_finalize_task_closeout()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse078_task_final$
BEGIN
    IF OLD.is_active = TRUE AND NEW.is_active = FALSE THEN
        UPDATE module001a_engineer_task_closeouts
        SET closeout_status = 'ptc_final_closed',
            ptc_final_closed_at = COALESCE(ptc_final_closed_at, NOW())
        WHERE task_id = NEW.task_id
          AND closeout_status <> 'ptc_final_closed';

        UPDATE project_assignments
        SET module001a_closeout_status = 'ptc_final_closed',
            module001a_closeout_updated_at = NOW()
        WHERE task_id = NEW.task_id
          AND module001a_closeout_status <> 'ptc_final_closed';

        INSERT INTO module001a_engineer_task_closeout_events (
            module001a_closeout_id, assignment_id, project_id, task_id,
            engineer_user_id, event_type, event_reason, evidence_json, occurred_at)
        SELECT closeout.module001a_closeout_id, closeout.assignment_id,
               closeout.project_id, closeout.task_id, closeout.engineer_user_id,
               'ptc_final_closed',
               'The original task was closed by the Module 055C task authority.',
               jsonb_build_object('taskActive', NEW.is_active, 'sourceModule', '055C'),
               NOW()
        FROM module001a_engineer_task_closeouts closeout
        WHERE closeout.task_id = NEW.task_id
          AND closeout.ptc_final_closed_at IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM module001a_engineer_task_closeout_events event
              WHERE event.module001a_closeout_id = closeout.module001a_closeout_id
                AND event.event_type = 'ptc_final_closed'
          );
    END IF;
    RETURN NEW;
END;
$projectpulse078_task_final$;

DROP TRIGGER IF EXISTS trg_module001a_closeout_touch_078 ON module001a_engineer_task_closeouts;
CREATE TRIGGER trg_module001a_closeout_touch_078
BEFORE UPDATE ON module001a_engineer_task_closeouts
FOR EACH ROW EXECUTE FUNCTION projectpulse078_touch_closeout();

DROP TRIGGER IF EXISTS trg_module001a_events_immutable_078 ON module001a_engineer_task_closeout_events;
CREATE TRIGGER trg_module001a_events_immutable_078
BEFORE UPDATE OR DELETE ON module001a_engineer_task_closeout_events
FOR EACH ROW EXECUTE FUNCTION projectpulse078_immutable_closeout_event();

DROP TRIGGER IF EXISTS trg_module001a_time_guard_078 ON time_entries;
CREATE TRIGGER trg_module001a_time_guard_078
BEFORE INSERT OR UPDATE OF user_id, project_id, task_id, hours ON time_entries
FOR EACH ROW EXECUTE FUNCTION projectpulse078_block_closed_assignment_time();

DROP TRIGGER IF EXISTS trg_module001a_project_final_078 ON projects;
CREATE TRIGGER trg_module001a_project_final_078
AFTER UPDATE OF status ON projects
FOR EACH ROW EXECUTE FUNCTION projectpulse078_finalize_project_closeouts();

DROP TRIGGER IF EXISTS trg_module001a_task_final_078 ON project_tasks;
CREATE TRIGGER trg_module001a_task_final_078
AFTER UPDATE OF is_active ON project_tasks
FOR EACH ROW EXECUTE FUNCTION projectpulse078_finalize_task_closeout();

CREATE TABLE IF NOT EXISTS module001a_078_permissions_created (
    app_permission_id UUID PRIMARY KEY REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    permission_code VARCHAR(100) NOT NULL UNIQUE,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS module001a_078_role_grants (
    app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE RESTRICT,
    app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (app_role_id, app_permission_id)
);

WITH inserted AS (
    INSERT INTO app_permissions(permission_code, permission_name, module_code, permission_description)
    VALUES
        ('VIEW_ENGINEER_TASK_CLOSEOUT_001A', 'View Engineer Request Closeout', '001A', 'View the authenticated Engineer''s active and historical Service Request, Pre-Sales, and Internal task closeouts.'),
        ('MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A', 'Manage own Engineer Request Closeout', '001A', 'Close and conditionally reopen only the authenticated Engineer''s eligible assigned tasks. Reopen is blocked after Module 055C final closure.')
    ON CONFLICT(permission_code) DO NOTHING
    RETURNING app_permission_id, permission_code
)
INSERT INTO module001a_078_permissions_created(app_permission_id, permission_code)
SELECT app_permission_id, permission_code FROM inserted
ON CONFLICT DO NOTHING;

WITH desired(role_code, permission_code) AS (
    VALUES
        ('SUPER_ADMINISTRATOR', 'VIEW_ENGINEER_TASK_CLOSEOUT_001A'),
        ('SUPER_ADMINISTRATOR', 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A'),
        ('ENGINEERING', 'VIEW_ENGINEER_TASK_CLOSEOUT_001A'),
        ('ENGINEERING', 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A'),
        ('ENGINEER', 'VIEW_ENGINEER_TASK_CLOSEOUT_001A'),
        ('ENGINEER', 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A'),
        ('SYSTEMS_ENGINEER', 'VIEW_ENGINEER_TASK_CLOSEOUT_001A'),
        ('SYSTEMS_ENGINEER', 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A'),
        ('NETWORK_ENGINEER', 'VIEW_ENGINEER_TASK_CLOSEOUT_001A'),
        ('NETWORK_ENGINEER', 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A'),
        ('ENTERPRISE_NETWORK_ENGINEER', 'VIEW_ENGINEER_TASK_CLOSEOUT_001A'),
        ('ENTERPRISE_NETWORK_ENGINEER', 'MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A')
), candidates AS (
    SELECT role.app_role_id, permission.app_permission_id
    FROM desired
    JOIN app_roles role ON UPPER(role.role_code) = desired.role_code AND role.is_active = TRUE
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
INSERT INTO module001a_078_role_grants(app_role_id, app_permission_id)
SELECT app_role_id, app_permission_id FROM inserted
ON CONFLICT DO NOTHING;

INSERT INTO app_feature_catalog(
    feature_code, feature_name, module_code, route_anchor,
    required_permission_code, feature_description, display_order, is_active)
VALUES(
    'ENGINEER_TASK_CLOSEOUT_001A',
    'Engineer Request Closeout',
    '001A',
    '#engineer-task-closeout',
    'VIEW_ENGINEER_TASK_CLOSEOUT_001A',
    'Engineer-owned Service Request, Pre-Sales, and Internal task closeout with billing lock, history, guarded reopen, and Module 065 notification to the Project Team Coordinator.',
    101,
    TRUE)
ON CONFLICT(feature_code) DO UPDATE
SET feature_name = EXCLUDED.feature_name,
    module_code = EXCLUDED.module_code,
    route_anchor = EXCLUDED.route_anchor,
    required_permission_code = EXCLUDED.required_permission_code,
    feature_description = EXCLUDED.feature_description,
    is_active = TRUE,
    updated_at = NOW();

INSERT INTO schema_migrations(migration_id, description, applied_at)
VALUES(
    '078_module_001a_engineer_request_closeout',
    'Add Engineer-owned request-task closeout, billing lock, immutable history, guarded reopen, scoped RBAC, and Module 065 notification evidence',
    NOW())
ON CONFLICT(migration_id) DO NOTHING;

COMMIT;
