-- ProjectPulse migration 070
-- Module 033 Project Forge: governed project-plan drafting, review, adoption,
-- scheduling, estimates, Module 065 notification contracts, and Module 064 AI routing.
--
-- This migration does not create sample projects, tasks, assignments, people, or
-- financial activity. Every Project Forge row is anchored to authoritative
-- ProjectPulse project, task, assignment, and user records.

BEGIN;

DO $projectpulse070_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.projects') IS NULL
       OR to_regclass('public.project_tasks') IS NULL
       OR to_regclass('public.project_assignments') IS NULL
       OR to_regclass('public.app_users') IS NULL
       OR to_regclass('public.app_roles') IS NULL
       OR to_regclass('public.app_permissions') IS NULL
       OR to_regclass('public.app_role_permissions') IS NULL
       OR to_regclass('public.app_feature_catalog') IS NULL THEN
        RAISE EXCEPTION 'Migration 070 requires the canonical project, identity, and RBAC foundations.';
    END IF;

    IF to_regclass('public.enterprise_notification_policies') IS NULL
       OR to_regclass('public.enterprise_notification_events') IS NULL THEN
        RAISE EXCEPTION 'Migration 070 requires Module 065 enterprise notification orchestration.';
    END IF;

    IF to_regclass('public.ai_capability_routes') IS NULL THEN
        RAISE EXCEPTION 'Migration 070 requires Module 064 AI capability routing.';
    END IF;
END;
$projectpulse070_prerequisites$;

-- AI output remains a reviewable draft until an authorized reviewer adopts it.
-- The plan stores only governed response evidence, citations, and warnings; it
-- does not store provider secrets or unrestricted source-document text.
CREATE TABLE IF NOT EXISTS project_forge_plans (
    plan_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    plan_name VARCHAR(240) NOT NULL CHECK (length(btrim(plan_name)) >= 3),
    objective TEXT NOT NULL DEFAULT '',
    plan_status VARCHAR(32) NOT NULL DEFAULT 'draft' CHECK (plan_status IN (
        'draft', 'in_review', 'reviewed', 'changes_requested',
        'approved', 'adopted', 'rejected', 'archived'
    )),
    source_kind VARCHAR(32) NOT NULL DEFAULT 'manual' CHECK (source_kind IN (
        'manual', 'ai_generated', 'canonical_snapshot'
    )),
    planned_start_date DATE NULL,
    planned_end_date DATE NULL,
    ai_capability_code VARCHAR(120) NOT NULL DEFAULT 'project_forge_plan_estimate',
    ai_provider_code VARCHAR(120) NOT NULL DEFAULT '',
    ai_correlation_id VARCHAR(180) NOT NULL DEFAULT '',
    ai_confidence NUMERIC(5,4) NULL CHECK (ai_confidence IS NULL OR ai_confidence BETWEEN 0 AND 1),
    ai_evidence JSONB NOT NULL DEFAULT '{}'::JSONB CHECK (jsonb_typeof(ai_evidence) = 'object'),
    ai_citations JSONB NOT NULL DEFAULT '[]'::JSONB CHECK (jsonb_typeof(ai_citations) = 'array'),
    ai_warnings JSONB NOT NULL DEFAULT '[]'::JSONB CHECK (jsonb_typeof(ai_warnings) = 'array'),
    review_notes TEXT NOT NULL DEFAULT '',
    reviewed_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    reviewed_at TIMESTAMPTZ NULL,
    adopted_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    adopted_at TIMESTAMPTZ NULL,
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    revision_number INTEGER NOT NULL DEFAULT 1 CHECK (revision_number >= 1),
    CONSTRAINT ck_project_forge_plan_dates CHECK (
        planned_end_date IS NULL OR planned_start_date IS NULL OR planned_end_date >= planned_start_date
    ),
    CONSTRAINT ck_project_forge_plan_review_evidence CHECK (
        (reviewed_at IS NULL AND reviewed_by_user_id IS NULL)
        OR (reviewed_at IS NOT NULL AND reviewed_by_user_id IS NOT NULL)
    ),
    CONSTRAINT ck_project_forge_plan_adoption_evidence CHECK (
        (adopted_at IS NULL AND adopted_by_user_id IS NULL)
        OR (adopted_at IS NOT NULL AND adopted_by_user_id IS NOT NULL)
    )
);

CREATE INDEX IF NOT EXISTS ix_project_forge_plans_project_status
    ON project_forge_plans(project_id, plan_status, updated_at DESC);
CREATE INDEX IF NOT EXISTS ix_project_forge_plans_created_by
    ON project_forge_plans(created_by_user_id, updated_at DESC);

-- Draft plan tasks are deliberately separate from canonical project_tasks.
-- canonical_task_id is populated only when a reviewed task is adopted.
CREATE TABLE IF NOT EXISTS project_forge_plan_tasks (
    plan_task_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    plan_id UUID NOT NULL REFERENCES project_forge_plans(plan_id) ON DELETE CASCADE,
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    wbs_code VARCHAR(100) NOT NULL CHECK (length(btrim(wbs_code)) > 0),
    parent_wbs_code VARCHAR(100) NOT NULL DEFAULT '',
    task_name VARCHAR(255) NOT NULL CHECK (length(btrim(task_name)) >= 3),
    task_description TEXT NOT NULL DEFAULT '',
    task_type VARCHAR(24) NOT NULL DEFAULT 'variable' CHECK (task_type IN ('variable', 'recurring')),
    phase_name VARCHAR(160) NOT NULL DEFAULT '',
    priority_code VARCHAR(24) NOT NULL DEFAULT 'normal' CHECK (priority_code IN (
        'low', 'normal', 'high', 'critical'
    )),
    task_status VARCHAR(32) NOT NULL DEFAULT 'draft' CHECK (task_status IN (
        'draft', 'in_review', 'approved', 'rejected',
        'not_started', 'in_progress', 'blocked', 'on_hold', 'completed', 'cancelled'
    )),
    kanban_category VARCHAR(32) NOT NULL DEFAULT 'backlog' CHECK (kanban_category IN (
        'backlog', 'ready', 'in_progress', 'review', 'blocked', 'done'
    )),
    decision_action VARCHAR(24) NOT NULL DEFAULT 'none' CHECK (decision_action IN (
        'none', 'do', 'delegate', 'decide', 'delete'
    )),
    planned_start_date DATE NULL,
    planned_end_date DATE NULL,
    duration_working_days INTEGER NULL CHECK (duration_working_days IS NULL OR duration_working_days >= 0),
    recurrence_rule JSONB NOT NULL DEFAULT '{}'::JSONB CHECK (jsonb_typeof(recurrence_rule) = 'object'),
    percent_complete NUMERIC(5,2) NOT NULL DEFAULT 0 CHECK (percent_complete BETWEEN 0 AND 100),
    estimated_hours NUMERIC(12,2) NOT NULL DEFAULT 0 CHECK (estimated_hours >= 0),
    hourly_rate NUMERIC(14,2) NOT NULL DEFAULT 0 CHECK (hourly_rate >= 0),
    material_units NUMERIC(14,2) NOT NULL DEFAULT 0 CHECK (material_units >= 0),
    material_unit_cost NUMERIC(14,2) NOT NULL DEFAULT 0 CHECK (material_unit_cost >= 0),
    fixed_cost NUMERIC(14,2) NOT NULL DEFAULT 0 CHECK (fixed_cost >= 0),
    travel_cost NUMERIC(14,2) NOT NULL DEFAULT 0 CHECK (travel_cost >= 0),
    equipment_cost NUMERIC(14,2) NOT NULL DEFAULT 0 CHECK (equipment_cost >= 0),
    miscellaneous_cost NUMERIC(14,2) NOT NULL DEFAULT 0 CHECK (miscellaneous_cost >= 0),
    is_important BOOLEAN NOT NULL DEFAULT FALSE,
    is_urgent BOOLEAN NOT NULL DEFAULT FALSE,
    reviewer_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    canonical_task_id UUID NULL REFERENCES project_tasks(task_id) ON DELETE SET NULL,
    source_kind VARCHAR(32) NOT NULL DEFAULT 'manual' CHECK (source_kind IN (
        'manual', 'ai_generated', 'canonical_snapshot'
    )),
    ai_correlation_id VARCHAR(180) NOT NULL DEFAULT '',
    display_order INTEGER NOT NULL DEFAULT 0 CHECK (display_order >= 0),
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    revision_number INTEGER NOT NULL DEFAULT 1 CHECK (revision_number >= 1),
    CONSTRAINT uq_project_forge_plan_task_wbs UNIQUE (plan_id, wbs_code),
    CONSTRAINT ck_project_forge_plan_task_dates CHECK (
        planned_end_date IS NULL OR planned_start_date IS NULL OR planned_end_date >= planned_start_date
    )
);

CREATE INDEX IF NOT EXISTS ix_project_forge_plan_tasks_plan_order
    ON project_forge_plan_tasks(plan_id, display_order, wbs_code);
CREATE INDEX IF NOT EXISTS ix_project_forge_plan_tasks_project_status
    ON project_forge_plan_tasks(project_id, task_status, planned_end_date NULLS LAST);
CREATE INDEX IF NOT EXISTS ix_project_forge_plan_tasks_reviewer
    ON project_forge_plan_tasks(reviewer_user_id, task_status, planned_end_date NULLS LAST)
    WHERE reviewer_user_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_project_forge_plan_tasks_canonical_task
    ON project_forge_plan_tasks(canonical_task_id)
    WHERE canonical_task_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS project_forge_plan_assignments (
    plan_assignment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    plan_id UUID NOT NULL REFERENCES project_forge_plans(plan_id) ON DELETE CASCADE,
    plan_task_id UUID NULL REFERENCES project_forge_plan_tasks(plan_task_id) ON DELETE CASCADE,
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    assignment_type VARCHAR(32) NOT NULL CHECK (assignment_type IN (
        'plan_reviewer', 'task_estimator', 'task_assignee'
    )),
    planned_hours NUMERIC(12,2) NOT NULL DEFAULT 0 CHECK (planned_hours >= 0),
    allocation_percent NUMERIC(5,2) NULL CHECK (allocation_percent IS NULL OR allocation_percent BETWEEN 0 AND 100),
    review_status VARCHAR(32) NOT NULL DEFAULT 'assigned' CHECK (review_status IN (
        'assigned', 'in_progress', 'completed', 'declined', 'reassigned'
    )),
    assignment_notes TEXT NOT NULL DEFAULT '',
    assigned_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    completed_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    revision_number INTEGER NOT NULL DEFAULT 1 CHECK (revision_number >= 1),
    CONSTRAINT uq_project_forge_plan_task_assignment UNIQUE (
        plan_task_id, user_id, assignment_type
    )
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_project_forge_plan_assignments_scope
    ON project_forge_plan_assignments(
        plan_id,
        (COALESCE(plan_task_id, '00000000-0000-0000-0000-000000000000'::UUID)),
        user_id,
        assignment_type
    );
CREATE INDEX IF NOT EXISTS ix_project_forge_plan_assignments_user_status
    ON project_forge_plan_assignments(user_id, review_status, updated_at DESC);

CREATE TABLE IF NOT EXISTS project_forge_task_dependencies (
    dependency_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    plan_id UUID NOT NULL REFERENCES project_forge_plans(plan_id) ON DELETE CASCADE,
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    predecessor_plan_task_id UUID NOT NULL REFERENCES project_forge_plan_tasks(plan_task_id) ON DELETE CASCADE,
    successor_plan_task_id UUID NOT NULL REFERENCES project_forge_plan_tasks(plan_task_id) ON DELETE CASCADE,
    dependency_type VARCHAR(8) NOT NULL DEFAULT 'FS' CHECK (dependency_type IN ('FS', 'SS', 'FF', 'SF')),
    lag_working_days INTEGER NOT NULL DEFAULT 0 CHECK (lag_working_days BETWEEN -3650 AND 3650),
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    revision_number INTEGER NOT NULL DEFAULT 1 CHECK (revision_number >= 1),
    CONSTRAINT ck_project_forge_dependency_not_self CHECK (predecessor_plan_task_id <> successor_plan_task_id),
    CONSTRAINT uq_project_forge_dependency UNIQUE (
        plan_id, predecessor_plan_task_id, successor_plan_task_id, dependency_type
    )
);

CREATE INDEX IF NOT EXISTS ix_project_forge_dependencies_successor
    ON project_forge_task_dependencies(plan_id, successor_plan_task_id);

-- Adopted canonical tasks receive Project Forge scheduling and cost metadata.
-- The canonical project_tasks row remains the system of record for task identity.
CREATE TABLE IF NOT EXISTS project_forge_task_details (
    task_id UUID PRIMARY KEY REFERENCES project_tasks(task_id) ON DELETE CASCADE,
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    source_plan_task_id UUID NULL REFERENCES project_forge_plan_tasks(plan_task_id) ON DELETE SET NULL,
    task_type VARCHAR(24) NOT NULL DEFAULT 'variable' CHECK (task_type IN ('variable', 'recurring')),
    phase_name VARCHAR(160) NOT NULL DEFAULT '',
    priority_code VARCHAR(24) NOT NULL DEFAULT 'normal' CHECK (priority_code IN ('low', 'normal', 'high', 'critical')),
    task_status VARCHAR(32) NOT NULL DEFAULT 'not_started' CHECK (task_status IN (
        'not_started', 'in_progress', 'blocked', 'on_hold', 'completed', 'cancelled'
    )),
    kanban_category VARCHAR(32) NOT NULL DEFAULT 'backlog' CHECK (kanban_category IN (
        'backlog', 'ready', 'in_progress', 'review', 'blocked', 'done'
    )),
    decision_action VARCHAR(24) NOT NULL DEFAULT 'none' CHECK (decision_action IN (
        'none', 'do', 'delegate', 'decide', 'delete'
    )),
    planned_start_date DATE NULL,
    planned_end_date DATE NULL,
    recurrence_rule JSONB NOT NULL DEFAULT '{}'::JSONB CHECK (jsonb_typeof(recurrence_rule) = 'object'),
    percent_complete NUMERIC(5,2) NOT NULL DEFAULT 0 CHECK (percent_complete BETWEEN 0 AND 100),
    estimated_hours NUMERIC(12,2) NOT NULL DEFAULT 0 CHECK (estimated_hours >= 0),
    hourly_rate NUMERIC(14,2) NOT NULL DEFAULT 0 CHECK (hourly_rate >= 0),
    material_units NUMERIC(14,2) NOT NULL DEFAULT 0 CHECK (material_units >= 0),
    material_unit_cost NUMERIC(14,2) NOT NULL DEFAULT 0 CHECK (material_unit_cost >= 0),
    fixed_cost NUMERIC(14,2) NOT NULL DEFAULT 0 CHECK (fixed_cost >= 0),
    travel_cost NUMERIC(14,2) NOT NULL DEFAULT 0 CHECK (travel_cost >= 0),
    equipment_cost NUMERIC(14,2) NOT NULL DEFAULT 0 CHECK (equipment_cost >= 0),
    miscellaneous_cost NUMERIC(14,2) NOT NULL DEFAULT 0 CHECK (miscellaneous_cost >= 0),
    is_important BOOLEAN NOT NULL DEFAULT FALSE,
    is_urgent BOOLEAN NOT NULL DEFAULT FALSE,
    source_kind VARCHAR(32) NOT NULL DEFAULT 'canonical' CHECK (source_kind IN (
        'canonical', 'pm_created', 'ai_draft'
    )),
    ai_correlation_id VARCHAR(180) NOT NULL DEFAULT '',
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    revision_number INTEGER NOT NULL DEFAULT 1 CHECK (revision_number >= 1),
    CONSTRAINT ck_project_forge_task_detail_dates CHECK (
        planned_end_date IS NULL OR planned_start_date IS NULL OR planned_end_date >= planned_start_date
    )
);

CREATE INDEX IF NOT EXISTS ix_project_forge_task_details_project_status
    ON project_forge_task_details(project_id, task_status, planned_end_date NULLS LAST);
CREATE UNIQUE INDEX IF NOT EXISTS ux_project_forge_task_details_source_plan_task
    ON project_forge_task_details(source_plan_task_id)
    WHERE source_plan_task_id IS NOT NULL;

-- Audit records are independent snapshots so deletion of an operational draft or
-- canonical task cannot erase or rewrite the historical evidence.
CREATE TABLE IF NOT EXISTS project_forge_audit_events (
    audit_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL,
    plan_id UUID NULL,
    plan_task_id UUID NULL,
    canonical_task_id UUID NULL,
    plan_assignment_id UUID NULL,
    event_code VARCHAR(100) NOT NULL,
    entity_type VARCHAR(60) NOT NULL,
    entity_id UUID NOT NULL,
    actual_actor_user_id UUID NULL,
    effective_actor_user_id UUID NULL,
    prior_state JSONB NULL,
    new_state JSONB NULL,
    event_metadata JSONB NOT NULL DEFAULT '{}'::JSONB CHECK (jsonb_typeof(event_metadata) = 'object'),
    correlation_id VARCHAR(180) NOT NULL DEFAULT '',
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_project_forge_audit_project_time
    ON project_forge_audit_events(project_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS ix_project_forge_audit_entity_time
    ON project_forge_audit_events(entity_type, entity_id, occurred_at DESC);

CREATE OR REPLACE FUNCTION projectpulse070_touch_revision()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse070_touch$
BEGIN
    NEW.updated_at := NOW();
    NEW.revision_number := OLD.revision_number + 1;
    RETURN NEW;
END;
$projectpulse070_touch$;

CREATE OR REPLACE FUNCTION projectpulse070_validate_plan_task()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse070_validate_plan_task$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM project_forge_plans plan
        WHERE plan.plan_id = NEW.plan_id
          AND plan.project_id = NEW.project_id
    ) THEN
        RAISE EXCEPTION 'Project Forge plan task project does not match its plan.';
    END IF;

    IF NEW.canonical_task_id IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM project_tasks task
        WHERE task.task_id = NEW.canonical_task_id
          AND task.project_id = NEW.project_id
    ) THEN
        RAISE EXCEPTION 'Adopted canonical task does not belong to the Project Forge project.';
    END IF;

    RETURN NEW;
END;
$projectpulse070_validate_plan_task$;

CREATE OR REPLACE FUNCTION projectpulse070_validate_plan_assignment()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse070_validate_plan_assignment$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM project_forge_plans plan
        WHERE plan.plan_id = NEW.plan_id
          AND plan.project_id = NEW.project_id
    ) THEN
        RAISE EXCEPTION 'Project Forge assignment project does not match its plan.';
    END IF;

    IF NEW.plan_task_id IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM project_forge_plan_tasks task
        WHERE task.plan_task_id = NEW.plan_task_id
          AND task.plan_id = NEW.plan_id
          AND task.project_id = NEW.project_id
    ) THEN
        RAISE EXCEPTION 'Project Forge assignment task does not match its plan and project.';
    END IF;

    IF NEW.assignment_type <> 'plan_reviewer' AND NEW.plan_task_id IS NULL THEN
        RAISE EXCEPTION 'Task estimator and task assignee records require a plan task.';
    END IF;

    RETURN NEW;
END;
$projectpulse070_validate_plan_assignment$;

CREATE OR REPLACE FUNCTION projectpulse070_validate_dependency()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse070_validate_dependency$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM project_forge_plans plan
        WHERE plan.plan_id = NEW.plan_id
          AND plan.project_id = NEW.project_id
    ) OR NOT EXISTS (
        SELECT 1 FROM project_forge_plan_tasks task
        WHERE task.plan_task_id = NEW.predecessor_plan_task_id
          AND task.plan_id = NEW.plan_id
          AND task.project_id = NEW.project_id
    ) OR NOT EXISTS (
        SELECT 1 FROM project_forge_plan_tasks task
        WHERE task.plan_task_id = NEW.successor_plan_task_id
          AND task.plan_id = NEW.plan_id
          AND task.project_id = NEW.project_id
    ) THEN
        RAISE EXCEPTION 'Project Forge dependency tasks must belong to the same plan and project.';
    END IF;

    RETURN NEW;
END;
$projectpulse070_validate_dependency$;

CREATE OR REPLACE FUNCTION projectpulse070_validate_task_detail()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse070_validate_task_detail$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM project_tasks task
        WHERE task.task_id = NEW.task_id
          AND task.project_id = NEW.project_id
    ) THEN
        RAISE EXCEPTION 'Project Forge task detail does not match its canonical project task.';
    END IF;

    IF NEW.source_plan_task_id IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM project_forge_plan_tasks task
        WHERE task.plan_task_id = NEW.source_plan_task_id
          AND task.project_id = NEW.project_id
          AND (task.canonical_task_id IS NULL OR task.canonical_task_id = NEW.task_id)
    ) THEN
        RAISE EXCEPTION 'Project Forge source plan task does not match the adopted canonical task.';
    END IF;

    RETURN NEW;
END;
$projectpulse070_validate_task_detail$;

CREATE OR REPLACE FUNCTION projectpulse070_record_audit_event()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse070_audit$
DECLARE
    row_before JSONB := CASE WHEN TG_OP IN ('UPDATE', 'DELETE') THEN to_jsonb(OLD) ELSE NULL END;
    row_after JSONB := CASE WHEN TG_OP IN ('INSERT', 'UPDATE') THEN to_jsonb(NEW) ELSE NULL END;
    source_row JSONB := COALESCE(row_after, row_before);
    audit_project_id UUID;
    audit_plan_id UUID;
    audit_plan_task_id UUID;
    audit_canonical_task_id UUID;
    audit_assignment_id UUID;
    audit_entity_id UUID;
    audit_entity_type TEXT;
    audit_actor_id UUID;
    audit_correlation_id TEXT := '';
BEGIN
    audit_project_id := (source_row ->> 'project_id')::UUID;
    audit_plan_id := NULLIF(source_row ->> 'plan_id', '')::UUID;
    audit_plan_task_id := NULLIF(source_row ->> 'plan_task_id', '')::UUID;
    audit_canonical_task_id := COALESCE(
        NULLIF(source_row ->> 'canonical_task_id', '')::UUID,
        CASE WHEN TG_TABLE_NAME = 'project_forge_task_details' THEN NULLIF(source_row ->> 'task_id', '')::UUID ELSE NULL END
    );
    audit_assignment_id := NULLIF(source_row ->> 'plan_assignment_id', '')::UUID;
    audit_actor_id := COALESCE(
        NULLIF(source_row ->> 'updated_by_user_id', '')::UUID,
        NULLIF(source_row ->> 'created_by_user_id', '')::UUID,
        NULLIF(source_row ->> 'assigned_by_user_id', '')::UUID
    );
    audit_correlation_id := COALESCE(source_row ->> 'ai_correlation_id', '');

    CASE TG_TABLE_NAME
        WHEN 'project_forge_plans' THEN
            audit_entity_type := 'plan';
            audit_entity_id := (source_row ->> 'plan_id')::UUID;
            audit_plan_id := audit_entity_id;
        WHEN 'project_forge_plan_tasks' THEN
            audit_entity_type := 'plan_task';
            audit_entity_id := (source_row ->> 'plan_task_id')::UUID;
            audit_plan_task_id := audit_entity_id;
        WHEN 'project_forge_plan_assignments' THEN
            audit_entity_type := 'plan_assignment';
            audit_entity_id := (source_row ->> 'plan_assignment_id')::UUID;
            audit_assignment_id := audit_entity_id;
        WHEN 'project_forge_task_dependencies' THEN
            audit_entity_type := 'task_dependency';
            audit_entity_id := (source_row ->> 'dependency_id')::UUID;
        WHEN 'project_forge_task_details' THEN
            audit_entity_type := 'canonical_task_detail';
            audit_entity_id := (source_row ->> 'task_id')::UUID;
        ELSE
            RAISE EXCEPTION 'Unsupported Project Forge audit source table: %', TG_TABLE_NAME;
    END CASE;

    INSERT INTO project_forge_audit_events (
        project_id, plan_id, plan_task_id, canonical_task_id, plan_assignment_id,
        event_code, entity_type, entity_id, actual_actor_user_id,
        effective_actor_user_id, prior_state, new_state, correlation_id
    ) VALUES (
        audit_project_id, audit_plan_id, audit_plan_task_id, audit_canonical_task_id,
        audit_assignment_id, audit_entity_type || '_' || lower(TG_OP),
        audit_entity_type, audit_entity_id, audit_actor_id, audit_actor_id,
        row_before, row_after, audit_correlation_id
    );

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$projectpulse070_audit$;

CREATE OR REPLACE FUNCTION projectpulse070_block_audit_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse070_immutable$
BEGIN
    RAISE EXCEPTION 'Project Forge audit evidence is append-only.';
END;
$projectpulse070_immutable$;

DROP TRIGGER IF EXISTS trg_project_forge_plans_revision ON project_forge_plans;
CREATE TRIGGER trg_project_forge_plans_revision
BEFORE UPDATE ON project_forge_plans
FOR EACH ROW EXECUTE FUNCTION projectpulse070_touch_revision();

DROP TRIGGER IF EXISTS trg_project_forge_plan_tasks_validate ON project_forge_plan_tasks;
CREATE TRIGGER trg_project_forge_plan_tasks_validate
BEFORE INSERT OR UPDATE ON project_forge_plan_tasks
FOR EACH ROW EXECUTE FUNCTION projectpulse070_validate_plan_task();
DROP TRIGGER IF EXISTS trg_project_forge_plan_tasks_revision ON project_forge_plan_tasks;
CREATE TRIGGER trg_project_forge_plan_tasks_revision
BEFORE UPDATE ON project_forge_plan_tasks
FOR EACH ROW EXECUTE FUNCTION projectpulse070_touch_revision();

DROP TRIGGER IF EXISTS trg_project_forge_plan_assignments_validate ON project_forge_plan_assignments;
CREATE TRIGGER trg_project_forge_plan_assignments_validate
BEFORE INSERT OR UPDATE ON project_forge_plan_assignments
FOR EACH ROW EXECUTE FUNCTION projectpulse070_validate_plan_assignment();
DROP TRIGGER IF EXISTS trg_project_forge_plan_assignments_revision ON project_forge_plan_assignments;
CREATE TRIGGER trg_project_forge_plan_assignments_revision
BEFORE UPDATE ON project_forge_plan_assignments
FOR EACH ROW EXECUTE FUNCTION projectpulse070_touch_revision();

DROP TRIGGER IF EXISTS trg_project_forge_dependencies_validate ON project_forge_task_dependencies;
CREATE TRIGGER trg_project_forge_dependencies_validate
BEFORE INSERT OR UPDATE ON project_forge_task_dependencies
FOR EACH ROW EXECUTE FUNCTION projectpulse070_validate_dependency();
DROP TRIGGER IF EXISTS trg_project_forge_dependencies_revision ON project_forge_task_dependencies;
CREATE TRIGGER trg_project_forge_dependencies_revision
BEFORE UPDATE ON project_forge_task_dependencies
FOR EACH ROW EXECUTE FUNCTION projectpulse070_touch_revision();

DROP TRIGGER IF EXISTS trg_project_forge_task_details_validate ON project_forge_task_details;
CREATE TRIGGER trg_project_forge_task_details_validate
BEFORE INSERT OR UPDATE ON project_forge_task_details
FOR EACH ROW EXECUTE FUNCTION projectpulse070_validate_task_detail();
DROP TRIGGER IF EXISTS trg_project_forge_task_details_revision ON project_forge_task_details;
CREATE TRIGGER trg_project_forge_task_details_revision
BEFORE UPDATE ON project_forge_task_details
FOR EACH ROW EXECUTE FUNCTION projectpulse070_touch_revision();

DROP TRIGGER IF EXISTS trg_project_forge_plans_audit ON project_forge_plans;
CREATE TRIGGER trg_project_forge_plans_audit
AFTER INSERT OR UPDATE OR DELETE ON project_forge_plans
FOR EACH ROW EXECUTE FUNCTION projectpulse070_record_audit_event();
DROP TRIGGER IF EXISTS trg_project_forge_plan_tasks_audit ON project_forge_plan_tasks;
CREATE TRIGGER trg_project_forge_plan_tasks_audit
AFTER INSERT OR UPDATE OR DELETE ON project_forge_plan_tasks
FOR EACH ROW EXECUTE FUNCTION projectpulse070_record_audit_event();
DROP TRIGGER IF EXISTS trg_project_forge_plan_assignments_audit ON project_forge_plan_assignments;
CREATE TRIGGER trg_project_forge_plan_assignments_audit
AFTER INSERT OR UPDATE OR DELETE ON project_forge_plan_assignments
FOR EACH ROW EXECUTE FUNCTION projectpulse070_record_audit_event();
DROP TRIGGER IF EXISTS trg_project_forge_dependencies_audit ON project_forge_task_dependencies;
CREATE TRIGGER trg_project_forge_dependencies_audit
AFTER INSERT OR UPDATE OR DELETE ON project_forge_task_dependencies
FOR EACH ROW EXECUTE FUNCTION projectpulse070_record_audit_event();
DROP TRIGGER IF EXISTS trg_project_forge_task_details_audit ON project_forge_task_details;
CREATE TRIGGER trg_project_forge_task_details_audit
AFTER INSERT OR UPDATE OR DELETE ON project_forge_task_details
FOR EACH ROW EXECUTE FUNCTION projectpulse070_record_audit_event();

DROP TRIGGER IF EXISTS trg_project_forge_audit_events_immutable ON project_forge_audit_events;
CREATE TRIGGER trg_project_forge_audit_events_immutable
BEFORE UPDATE OR DELETE ON project_forge_audit_events
FOR EACH ROW EXECUTE FUNCTION projectpulse070_block_audit_mutation();

-- Evidence tables allow rollback to remove only catalog rows and grants that this
-- migration introduced, without overwriting later administrator configuration.
CREATE TABLE IF NOT EXISTS project_forge_070_permissions_created (
    app_permission_id UUID PRIMARY KEY REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    permission_code VARCHAR(100) NOT NULL UNIQUE,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE IF NOT EXISTS project_forge_070_role_grants (
    app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE RESTRICT,
    app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (app_role_id, app_permission_id)
);
CREATE TABLE IF NOT EXISTS project_forge_070_notification_policies_created (
    policy_code VARCHAR(160) PRIMARY KEY,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE IF NOT EXISTS project_forge_070_ai_routes_created (
    feature_code TEXT PRIMARY KEY,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE IF NOT EXISTS project_forge_070_features_created (
    feature_code VARCHAR(100) PRIMARY KEY,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

WITH inserted_permissions AS (
    INSERT INTO app_permissions (
        permission_code, permission_name, module_code, permission_description
    ) VALUES
        ('VIEW_PROJECT_FORGE_033', 'View Project Forge', '033', 'View Project Forge plans and live project information within authoritative project scope.'),
        ('MANAGE_PROJECT_FORGE_033', 'Manage Project Forge', '033', 'Create, review, adopt, schedule, and update Project Forge plans and tasks within authoritative project scope.'),
        ('EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033', 'Edit assigned Project Forge estimates', '033', 'Modify estimate fields only for Project Forge plan tasks explicitly assigned to the current engineer.'),
        ('USE_PROJECT_FORGE_AI_033', 'Use Project Forge AI', '033', 'Generate reviewable project-plan, task, dependency, and estimate drafts through Module 064 provider governance.')
    ON CONFLICT (permission_code) DO NOTHING
    RETURNING app_permission_id, permission_code
)
INSERT INTO project_forge_070_permissions_created(app_permission_id, permission_code)
SELECT app_permission_id, permission_code
FROM inserted_permissions
ON CONFLICT DO NOTHING;

WITH desired_grants(role_code, permission_code) AS (
    VALUES
        ('SUPER_ADMINISTRATOR', 'VIEW_PROJECT_FORGE_033'),
        ('SUPER_ADMINISTRATOR', 'MANAGE_PROJECT_FORGE_033'),
        ('SUPER_ADMINISTRATOR', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'),
        ('SUPER_ADMINISTRATOR', 'USE_PROJECT_FORGE_AI_033'),
        ('ADMINISTRATOR', 'VIEW_PROJECT_FORGE_033'),
        ('ADMINISTRATOR', 'MANAGE_PROJECT_FORGE_033'),
        ('ADMINISTRATOR', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'),
        ('ADMINISTRATOR', 'USE_PROJECT_FORGE_AI_033'),
        ('PROJECT_MANAGER', 'VIEW_PROJECT_FORGE_033'),
        ('PROJECT_MANAGER', 'MANAGE_PROJECT_FORGE_033'),
        ('PROJECT_MANAGER', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'),
        ('PROJECT_MANAGER', 'USE_PROJECT_FORGE_AI_033'),
        ('PROJECT_MANAGEMENT', 'VIEW_PROJECT_FORGE_033'),
        ('PROJECT_MANAGEMENT', 'MANAGE_PROJECT_FORGE_033'),
        ('PROJECT_MANAGEMENT', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'),
        ('PROJECT_MANAGEMENT', 'USE_PROJECT_FORGE_AI_033'),
        ('PROJECT_MANAGEMENT_LEAD', 'VIEW_PROJECT_FORGE_033'),
        ('PROJECT_MANAGEMENT_LEAD', 'MANAGE_PROJECT_FORGE_033'),
        ('PROJECT_MANAGEMENT_LEAD', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'),
        ('PROJECT_MANAGEMENT_LEAD', 'USE_PROJECT_FORGE_AI_033'),
        ('PROJECT_MANAGEMENT_TEAM_LEAD', 'VIEW_PROJECT_FORGE_033'),
        ('PROJECT_MANAGEMENT_TEAM_LEAD', 'MANAGE_PROJECT_FORGE_033'),
        ('PROJECT_MANAGEMENT_TEAM_LEAD', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'),
        ('PROJECT_MANAGEMENT_TEAM_LEAD', 'USE_PROJECT_FORGE_AI_033'),
        ('PM_TEAM_LEAD', 'VIEW_PROJECT_FORGE_033'),
        ('PM_TEAM_LEAD', 'MANAGE_PROJECT_FORGE_033'),
        ('PM_TEAM_LEAD', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'),
        ('PM_TEAM_LEAD', 'USE_PROJECT_FORGE_AI_033'),
        ('ENGINEERING', 'VIEW_PROJECT_FORGE_033'),
        ('ENGINEERING', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'),
        ('ENGINEER', 'VIEW_PROJECT_FORGE_033'),
        ('ENGINEER', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'),
        ('ENGINEERING_LEAD', 'VIEW_PROJECT_FORGE_033'),
        ('ENGINEERING_LEAD', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'),
        ('ENGINEERING_TEAM_LEAD', 'VIEW_PROJECT_FORGE_033'),
        ('ENGINEERING_TEAM_LEAD', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'),
        ('SYSTEMS_ENGINEER', 'VIEW_PROJECT_FORGE_033'),
        ('SYSTEMS_ENGINEER', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'),
        ('NETWORK_ENGINEER', 'VIEW_PROJECT_FORGE_033'),
        ('NETWORK_ENGINEER', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'),
        ('ENTERPRISE_NETWORK_ENGINEER', 'VIEW_PROJECT_FORGE_033'),
        ('ENTERPRISE_NETWORK_ENGINEER', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033')
), candidates AS (
    SELECT role.app_role_id, permission.app_permission_id
    FROM desired_grants desired
    JOIN app_roles role
      ON upper(role.role_code) = desired.role_code
     AND role.is_active = TRUE
    JOIN app_permissions permission
      ON permission.permission_code = desired.permission_code
    LEFT JOIN app_role_permissions existing
      ON existing.app_role_id = role.app_role_id
     AND existing.app_permission_id = permission.app_permission_id
    WHERE existing.app_role_permission_id IS NULL
), inserted_grants AS (
    INSERT INTO app_role_permissions(app_role_id, app_permission_id, created_at)
    SELECT app_role_id, app_permission_id, NOW()
    FROM candidates
    ON CONFLICT (app_role_id, app_permission_id) DO NOTHING
    RETURNING app_role_id, app_permission_id
)
INSERT INTO project_forge_070_role_grants(app_role_id, app_permission_id)
SELECT app_role_id, app_permission_id
FROM inserted_grants
ON CONFLICT DO NOTHING;

WITH inserted_policies AS (
    INSERT INTO enterprise_notification_policies (
        policy_code, policy_name, category, source_module, event_code,
        trigger_mode, recipient_strategy, trigger_configuration,
        recipient_configuration, severity, acknowledgement_required,
        acknowledgement_escalation_minutes, subject_template, text_template,
        producer_contract, source_state
    ) VALUES
        ('PROJECT_FORGE_REVIEW_ASSIGNED', 'Project Forge review assigned', 'project', '033', 'project_forge_review_assigned', 'event', 'project_team', '{}', '{"to":["reviewer","project_team"]}', 'informational', FALSE, NULL, 'ProjectPulse: Project Forge review assigned — {{projectCode}}', '{{reviewerName}} was assigned to review {{planName}} for {{projectCode}} {{projectName}}.', 'module_033_native_event', 'contract_ready'),
        ('PROJECT_FORGE_TASK_ASSIGNED', 'Project Forge task assigned', 'project', '033', 'project_forge_task_assigned', 'event', 'project_team', '{}', '{"to":["assignee","project_team"]}', 'informational', FALSE, NULL, 'ProjectPulse: Project Forge task assigned — {{projectCode}}', '{{taskName}} was assigned to {{assigneeName}} for {{projectCode}} {{projectName}}.', 'module_033_native_event', 'contract_ready'),
        ('PROJECT_FORGE_TASK_UPDATED', 'Project Forge task updated', 'project', '033', 'project_forge_task_updated', 'event', 'project_team', '{}', '{"to":["project_team"]}', 'informational', FALSE, NULL, 'ProjectPulse: Project Forge task updated — {{projectCode}}', '{{taskName}} was updated by {{updatedByName}}. {{changeSummary}}', 'module_033_native_event', 'contract_ready'),
        ('PROJECT_FORGE_PLAN_UPDATED', 'Project Forge plan updated', 'project', '033', 'project_forge_plan_updated', 'event', 'project_team', '{}', '{"to":["project_team"]}', 'informational', FALSE, NULL, 'ProjectPulse: Project Forge plan updated — {{projectCode}}', '{{planName}} was updated by {{updatedByName}}. {{changeSummary}}', 'module_033_native_event', 'contract_ready')
    ON CONFLICT (policy_code) DO NOTHING
    RETURNING policy_code
)
INSERT INTO project_forge_070_notification_policies_created(policy_code)
SELECT policy_code FROM inserted_policies
ON CONFLICT DO NOTHING;

WITH inserted_route AS (
    INSERT INTO ai_capability_routes (
        feature_code, route_targets, external_context_policy, revision, updated_at, updated_by
    ) VALUES (
        'project_forge_plan_estimate',
        '["celar_ai","claude","openai","local_template"]'::JSONB,
        'sanitized_generic_only',
        1,
        NOW(),
        NULL
    )
    ON CONFLICT (feature_code) DO NOTHING
    RETURNING feature_code
)
INSERT INTO project_forge_070_ai_routes_created(feature_code)
SELECT feature_code FROM inserted_route
ON CONFLICT DO NOTHING;

WITH inserted_feature AS (
    INSERT INTO app_feature_catalog (
        feature_code, feature_name, module_code, route_anchor,
        required_permission_code, feature_description, display_order, is_active
    ) VALUES (
        'PROJECT_FORGE',
        'Project Forge',
        '033',
        '#project-forge',
        'VIEW_PROJECT_FORGE_033',
        'Workbook-complete project planning, review, estimating, scheduling, Kanban, Gantt, calendar, budget, and decision workspace backed by live ProjectPulse projects.',
        330,
        TRUE
    )
    ON CONFLICT (feature_code) DO NOTHING
    RETURNING feature_code
)
INSERT INTO project_forge_070_features_created(feature_code)
SELECT feature_code FROM inserted_feature
ON CONFLICT DO NOTHING;

COMMENT ON TABLE project_forge_plans IS
    'Module 033 review-first project plan drafts; AI output remains non-canonical until authorized adoption.';
COMMENT ON TABLE project_forge_plan_tasks IS
    'Reviewable Project Forge plan tasks; canonical_task_id is populated only after adoption.';
COMMENT ON TABLE project_forge_plan_assignments IS
    'Project Forge plan-review, task-estimate, and future canonical task assignment intent tied to authoritative users.';
COMMENT ON TABLE project_forge_audit_events IS
    'Append-only Project Forge mutation evidence independent from deletable operational rows.';

INSERT INTO schema_migrations(migration_id, description, applied_at)
VALUES (
    '070_module_033_project_forge',
    'Add Module 033 Project Forge review-first plans, task estimates, dependencies, assignments, canonical adoption metadata, audit evidence, RBAC, Module 065 policies, and Module 064 AI routing without sample project data',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
