-- Pulse migration 093
-- Complete the non-Super-Administrator Modules-page and owner-catalog repair,
-- and make Module 055C assignments canonical for Modules 019, 001A, and 001.
--
-- This migration is additive and idempotent. It does not hard-code any Service
-- Request, Presales, Internal Task, engineer, email address, or environment.

BEGIN;

DO $projectpulse093_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.app_users') IS NULL
       OR to_regclass('public.projects') IS NULL
       OR to_regclass('public.project_tasks') IS NULL
       OR to_regclass('public.project_assignments') IS NULL
       OR to_regclass('public.work_register_task_assignment_history') IS NULL
       OR to_regclass('public.scoped_role_policy_modules') IS NULL THEN
        RAISE EXCEPTION
            'Migration 093 requires the identity, project, task, canonical assignment, Work Register assignment-history, and module-catalog foundations.';
    END IF;
END;
$projectpulse093_prerequisites$;

-- Keep module-owner metadata optional and durable. A missing owner column must
-- never cause /api/module-catalog/owners to become a blocking dependency for
-- the Modules directory.
ALTER TABLE scoped_role_policy_modules
    ADD COLUMN IF NOT EXISTS owner_user_id UUID NULL,
    ADD COLUMN IF NOT EXISTS owner_revision_number INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS owner_updated_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS owner_updated_by_user_id UUID NULL;

DO $projectpulse093_owner_constraints$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_scoped_role_policy_modules_owner_user'
          AND conrelid = 'public.scoped_role_policy_modules'::regclass
    ) THEN
        ALTER TABLE scoped_role_policy_modules
            ADD CONSTRAINT fk_scoped_role_policy_modules_owner_user
            FOREIGN KEY (owner_user_id)
            REFERENCES app_users(user_id)
            ON DELETE SET NULL;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_scoped_role_policy_modules_owner_updated_by'
          AND conrelid = 'public.scoped_role_policy_modules'::regclass
    ) THEN
        ALTER TABLE scoped_role_policy_modules
            ADD CONSTRAINT fk_scoped_role_policy_modules_owner_updated_by
            FOREIGN KEY (owner_updated_by_user_id)
            REFERENCES app_users(user_id)
            ON DELETE SET NULL;
    END IF;
END;
$projectpulse093_owner_constraints$;

CREATE INDEX IF NOT EXISTS ix_scoped_role_policy_modules_owner
    ON scoped_role_policy_modules(owner_user_id, is_active, module_code);

-- Migration 089 was registered in some environments before Modules 031, 032,
-- and 033 were added to its canonical catalog. Those environments can render
-- the frontend modules while lacking the scoped_role_policy_modules rows that
-- the owner-update API locks and updates. Reconcile the three canonical rows
-- without replacing an existing owner or owner revision.
CREATE TABLE IF NOT EXISTS module_catalog_reconciliation_093_owner_repair_evidence (
    module_code TEXT PRIMARY KEY,
    was_present BOOLEAN NOT NULL,
    previous_module_name TEXT NULL,
    previous_route_scope TEXT NULL,
    previous_current_state TEXT NULL,
    previous_permission_notes TEXT NULL,
    previous_source_url TEXT NULL,
    previous_is_active BOOLEAN NULL,
    previous_owner_user_id UUID NULL,
    previous_owner_revision_number INTEGER NULL,
    repaired_owner_user_id UUID NULL,
    repaired_owner_revision_number INTEGER NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TEMP TABLE projectpulse093_required_owner_catalog (
    module_code TEXT PRIMARY KEY,
    module_name TEXT NOT NULL,
    route_scope TEXT NOT NULL,
    module_group TEXT NOT NULL
) ON COMMIT DROP;

INSERT INTO projectpulse093_required_owner_catalog (
    module_code,
    module_name,
    route_scope,
    module_group
)
VALUES
    ('031', 'Financial Operations Workbench', 'financial-operations-workbench', 'Reports & Workflow'),
    ('032', 'Notification Delivery Monitor', 'notification-delivery-monitor', 'Reports & Workflow'),
    ('033', 'Project Forge', 'project-forge', 'Project Delivery');

INSERT INTO module_catalog_reconciliation_093_owner_repair_evidence (
    module_code,
    was_present,
    previous_module_name,
    previous_route_scope,
    previous_current_state,
    previous_permission_notes,
    previous_source_url,
    previous_is_active,
    previous_owner_user_id,
    previous_owner_revision_number
)
SELECT
    required.module_code,
    existing.module_code IS NOT NULL,
    existing.module_name,
    existing.route_scope,
    existing.current_state,
    existing.permission_notes,
    existing.source_url,
    existing.is_active,
    existing.owner_user_id,
    existing.owner_revision_number
FROM projectpulse093_required_owner_catalog required
LEFT JOIN scoped_role_policy_modules existing
  ON upper(existing.module_code) = required.module_code
ON CONFLICT (module_code) DO NOTHING;

INSERT INTO scoped_role_policy_modules (
    module_code,
    module_name,
    route_scope,
    current_state,
    permission_notes,
    source_url,
    is_active
)
SELECT
    required.module_code,
    required.module_name,
    required.route_scope,
    'Installed',
    'Canonical Pulse module catalog entry · ' || required.module_group || '. Reconciled by Migration 093.',
    'src/frontend/project-time-web/src/module-availability-registry.js',
    TRUE
FROM projectpulse093_required_owner_catalog required
ON CONFLICT (module_code) DO UPDATE
SET module_name = EXCLUDED.module_name,
    route_scope = EXCLUDED.route_scope,
    current_state = EXCLUDED.current_state,
    permission_notes = EXCLUDED.permission_notes,
    source_url = EXCLUDED.source_url,
    is_active = TRUE;

DO $projectpulse093_assign_repaired_catalog_owner$
DECLARE
    default_owner_user_id UUID;
BEGIN
    SELECT module.owner_user_id
    INTO default_owner_user_id
    FROM scoped_role_policy_modules module
    JOIN app_users owner_user
      ON owner_user.user_id = module.owner_user_id
     AND owner_user.is_active = TRUE
    WHERE module.is_active = TRUE
      AND module.owner_user_id IS NOT NULL
      AND upper(module.module_code) NOT IN ('031', '032', '033')
    GROUP BY module.owner_user_id
    ORDER BY COUNT(*) DESC, module.owner_user_id
    LIMIT 1;

    IF default_owner_user_id IS NOT NULL THEN
        UPDATE scoped_role_policy_modules module
        SET owner_user_id = default_owner_user_id,
  owner_revision_number = COALESCE(module.owner_revision_number, 0) + 1,
  owner_updated_at = NOW(),
  owner_updated_by_user_id = default_owner_user_id
        FROM module_catalog_reconciliation_093_owner_repair_evidence evidence
        WHERE upper(module.module_code) = evidence.module_code
AND evidence.was_present = FALSE
AND evidence.repaired_owner_revision_number IS NULL
AND module.owner_user_id IS NULL;
    END IF;

    UPDATE module_catalog_reconciliation_093_owner_repair_evidence evidence
    SET repaired_owner_user_id = module.owner_user_id,
        repaired_owner_revision_number = COALESCE(module.owner_revision_number, 0)
    FROM scoped_role_policy_modules module
    WHERE upper(module.module_code) = evidence.module_code
      AND evidence.repaired_owner_revision_number IS NULL;
END;
$projectpulse093_assign_repaired_catalog_owner$;

DO $projectpulse093_verify_owner_catalog_repair$
DECLARE
    invalid_modules TEXT[];
BEGIN
    SELECT array_agg(required.module_code ORDER BY required.module_code)
    INTO invalid_modules
    FROM projectpulse093_required_owner_catalog required
    LEFT JOIN scoped_role_policy_modules module
      ON upper(module.module_code) = required.module_code
    WHERE module.module_code IS NULL
       OR module.is_active IS DISTINCT FROM TRUE
       OR module.module_name IS DISTINCT FROM required.module_name
       OR module.route_scope IS DISTINCT FROM required.route_scope;

    IF invalid_modules IS NOT NULL THEN
        RAISE EXCEPTION
  'Migration 093 module catalog repair did not restore active canonical row(s): %',
  array_to_string(invalid_modules, ', ');
    END IF;
END;
$projectpulse093_verify_owner_catalog_repair$;

-- Preserve the canonical project_assignments contract consumed directly by:
--   Module 019  Project Engineering Workspace
--   Module 001A Engineer Request Closeout
--   Module 001  Timesheet work queue and task association
ALTER TABLE project_assignments
    ADD COLUMN IF NOT EXISTS assigned_hours NUMERIC(10,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS assignment_source TEXT NOT NULL DEFAULT 'project_assignments',
    ADD COLUMN IF NOT EXISTS assignment_notes TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW();

CREATE OR REPLACE FUNCTION projectpulse093_normalized_task_reference(p_value TEXT)
RETURNS TEXT
LANGUAGE sql
IMMUTABLE
AS $projectpulse093_normalized_task_reference$
    SELECT regexp_replace(
        lower(btrim(COALESCE(p_value, ''))),
        '[^a-z0-9]+',
        '',
        'g'
    );
$projectpulse093_normalized_task_reference$;

-- Module 055C has historically persisted either the canonical task UUID or a
-- durable task identifier/code in task_id_text. Resolve both without guessing
-- by task name. Project scope is mandatory, so equal task codes in other
-- projects can never authorize an assignment.
CREATE OR REPLACE FUNCTION projectpulse093_resolve_project_task_id(
    p_project_id UUID,
    p_task_reference TEXT
)
RETURNS UUID
LANGUAGE sql
STABLE
AS $projectpulse093_resolve_project_task_id$
    SELECT task.task_id
    FROM project_tasks task
    WHERE task.project_id = p_project_id
      AND NULLIF(btrim(COALESCE(p_task_reference, '')), '') IS NOT NULL
      AND (
            task.task_id::TEXT = btrim(p_task_reference)
         OR lower(COALESCE(task.task_code, '')) = lower(btrim(p_task_reference))
         OR projectpulse093_normalized_task_reference(task.task_code)
              = projectpulse093_normalized_task_reference(p_task_reference)
         OR lower(COALESCE(to_jsonb(task)->>'project_task_id', ''))
              = lower(btrim(p_task_reference))
         OR lower(COALESCE(to_jsonb(task)->>'work_task_id', ''))
              = lower(btrim(p_task_reference))
         OR lower(COALESCE(to_jsonb(task)->>'source_task_id', ''))
              = lower(btrim(p_task_reference))
         OR lower(COALESCE(to_jsonb(task)->>'task_identifier', ''))
              = lower(btrim(p_task_reference))
         OR projectpulse093_normalized_task_reference(
                COALESCE(to_jsonb(task)->>'work_task_code', '')
            ) = projectpulse093_normalized_task_reference(p_task_reference)
      )
    ORDER BY
        CASE
            WHEN task.task_id::TEXT = btrim(p_task_reference) THEN 0
            WHEN lower(COALESCE(task.task_code, '')) = lower(btrim(p_task_reference)) THEN 1
            WHEN projectpulse093_normalized_task_reference(task.task_code)
                 = projectpulse093_normalized_task_reference(p_task_reference) THEN 2
            ELSE 3
        END,
        task.created_at,
        task.task_id
    LIMIT 1;
$projectpulse093_resolve_project_task_id$;

CREATE OR REPLACE FUNCTION projectpulse093_sync_work_register_assignment()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse093_sync_work_register_assignment$
DECLARE
    v_new_task_id UUID;
    v_old_task_id UUID;
    v_new_effective_start DATE;
    v_new_is_active BOOLEAN := FALSE;
    v_task_is_active BOOLEAN := FALSE;
    v_project_is_active BOOLEAN := FALSE;
BEGIN
    v_new_task_id := projectpulse093_resolve_project_task_id(
        NEW.project_id,
        NEW.task_id_text
    );
    v_new_effective_start := COALESCE(NEW.effective_start_date, CURRENT_DATE);

    IF TG_OP = 'UPDATE' THEN
        v_old_task_id := projectpulse093_resolve_project_task_id(
            OLD.project_id,
            OLD.task_id_text
        );

        IF OLD.assigned_user_id IS NOT NULL
           AND v_old_task_id IS NOT NULL
           AND (
                OLD.project_id IS DISTINCT FROM NEW.project_id
                OR v_old_task_id IS DISTINCT FROM v_new_task_id
                OR OLD.assigned_user_id IS DISTINCT FROM NEW.assigned_user_id
                OR COALESCE(OLD.effective_start_date, CURRENT_DATE)
                    IS DISTINCT FROM v_new_effective_start
                OR lower(btrim(COALESCE(NEW.assignment_status, ''))) <> 'active'
                OR (
                    NEW.effective_end_date IS NOT NULL
                    AND NEW.effective_end_date < CURRENT_DATE
                )
           ) THEN
            UPDATE project_assignments assignment
            SET effective_end_date = LEAST(
                    COALESCE(
                        NEW.effective_end_date,
                        v_new_effective_start - 1,
                        CURRENT_DATE - 1
                    ),
                    CURRENT_DATE - 1
                ),
                assignment_source = 'work_register_assignment_history_v2',
                assignment_notes = COALESCE(
                    NULLIF(NEW.change_reason, ''),
                    'Assignment closed from Module 055C task assignment history.'
                ),
                updated_at = NOW()
            WHERE assignment.project_id = OLD.project_id
              AND assignment.task_id = v_old_task_id
              AND assignment.user_id = OLD.assigned_user_id
              AND (
                    assignment.effective_end_date IS NULL
                    OR assignment.effective_end_date >= CURRENT_DATE
              );
        END IF;
    END IF;

    IF NEW.assigned_user_id IS NULL OR v_new_task_id IS NULL THEN
        RETURN NEW;
    END IF;

    SELECT
        task.is_active,
        lower(btrim(COALESCE(project.status, 'active'))) NOT IN (
            'closed',
            'complete',
            'completed',
            'done',
            'cancelled',
            'canceled',
            'archived'
        )
    INTO v_task_is_active, v_project_is_active
    FROM project_tasks task
    JOIN projects project
      ON project.project_id = task.project_id
    WHERE task.project_id = NEW.project_id
      AND task.task_id = v_new_task_id;

    v_new_is_active :=
        lower(btrim(COALESCE(NEW.assignment_status, ''))) = 'active'
        AND (NEW.effective_end_date IS NULL OR NEW.effective_end_date >= CURRENT_DATE)
        AND COALESCE(v_task_is_active, FALSE)
        AND COALESCE(v_project_is_active, FALSE);

    IF v_new_is_active THEN
        INSERT INTO project_assignments (
            project_id,
            task_id,
            user_id,
            assigned_by_user_id,
            effective_start_date,
            effective_end_date,
            allocation_percent,
            assigned_hours,
            assignment_source,
            assignment_notes,
            updated_at
        )
        VALUES (
            NEW.project_id,
            v_new_task_id,
            NEW.assigned_user_id,
            NEW.changed_by_user_id,
            v_new_effective_start,
            NEW.effective_end_date,
            NULLIF(NEW.allocation_percent, 0),
            COALESCE(NEW.allocated_hours, 0),
            'work_register_assignment_history_v2',
            COALESCE(
                NULLIF(NEW.change_reason, ''),
                'Synchronized from Module 055C task assignment history.'
            ),
            NOW()
        )
        ON CONFLICT (project_id, task_id, user_id, effective_start_date)
        DO UPDATE SET
            assigned_by_user_id = COALESCE(
                EXCLUDED.assigned_by_user_id,
                project_assignments.assigned_by_user_id
            ),
            effective_end_date = EXCLUDED.effective_end_date,
            allocation_percent = EXCLUDED.allocation_percent,
            assigned_hours = EXCLUDED.assigned_hours,
            assignment_source = EXCLUDED.assignment_source,
            assignment_notes = EXCLUDED.assignment_notes,
            updated_at = NOW();
    ELSE
        UPDATE project_assignments assignment
        SET effective_end_date = LEAST(
                COALESCE(NEW.effective_end_date, CURRENT_DATE - 1),
                CURRENT_DATE - 1
            ),
            assignment_source = 'work_register_assignment_history_v2',
            assignment_notes = COALESCE(
                NULLIF(NEW.change_reason, ''),
                'Assignment closed because the Module 055C task or project is no longer active.'
            ),
            updated_at = NOW()
        WHERE assignment.project_id = NEW.project_id
          AND assignment.task_id = v_new_task_id
          AND assignment.user_id = NEW.assigned_user_id
          AND (
                assignment.effective_end_date IS NULL
                OR assignment.effective_end_date >= CURRENT_DATE
          );
    END IF;

    RETURN NEW;
END;
$projectpulse093_sync_work_register_assignment$;

DROP TRIGGER IF EXISTS trg_projectpulse092_sync_work_register_assignment
    ON work_register_task_assignment_history;
DROP TRIGGER IF EXISTS trg_projectpulse093_sync_work_register_assignment
    ON work_register_task_assignment_history;

CREATE TRIGGER trg_projectpulse093_sync_work_register_assignment
AFTER INSERT OR UPDATE OF
    project_id,
    task_id_text,
    assigned_user_id,
    assignment_status,
    effective_start_date,
    effective_end_date,
    allocated_hours,
    allocation_percent,
    change_reason
ON work_register_task_assignment_history
FOR EACH ROW
EXECUTE FUNCTION projectpulse093_sync_work_register_assignment();

-- If a task is created after its Work Register assignment-history row, or its
-- durable code becomes resolvable later, re-run the canonical bridge
-- immediately. The same mechanism closes the assignment when a task is made
-- inactive.
CREATE OR REPLACE FUNCTION projectpulse093_resync_history_after_task_change()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse093_resync_history_after_task_change$
BEGIN
    UPDATE work_register_task_assignment_history history
    SET task_id_text = history.task_id_text
    WHERE history.project_id = NEW.project_id
      AND history.assigned_user_id IS NOT NULL
      AND lower(btrim(COALESCE(history.assignment_status, ''))) = 'active'
      AND projectpulse093_resolve_project_task_id(
            history.project_id,
            history.task_id_text
          ) = NEW.task_id;

    RETURN NEW;
END;
$projectpulse093_resync_history_after_task_change$;

DROP TRIGGER IF EXISTS trg_projectpulse093_resync_history_after_task_change
    ON project_tasks;

CREATE TRIGGER trg_projectpulse093_resync_history_after_task_change
AFTER INSERT OR UPDATE OF task_code, is_active
ON project_tasks
FOR EACH ROW
EXECUTE FUNCTION projectpulse093_resync_history_after_task_change();

-- A terminal project lifecycle must remove bridge-owned assignments from all
-- three consuming modules without waiting for a later task edit.
CREATE OR REPLACE FUNCTION projectpulse093_resync_history_after_project_status()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse093_resync_history_after_project_status$
BEGIN
    IF lower(btrim(COALESCE(OLD.status, '')))
       IS DISTINCT FROM lower(btrim(COALESCE(NEW.status, ''))) THEN
        UPDATE work_register_task_assignment_history history
        SET task_id_text = history.task_id_text
        WHERE history.project_id = NEW.project_id
          AND history.assigned_user_id IS NOT NULL
          AND lower(btrim(COALESCE(history.assignment_status, ''))) = 'active';
    END IF;

    RETURN NEW;
END;
$projectpulse093_resync_history_after_project_status$;

DROP TRIGGER IF EXISTS trg_projectpulse093_resync_history_after_project_status
    ON projects;

CREATE TRIGGER trg_projectpulse093_resync_history_after_project_status
AFTER UPDATE OF status
ON projects
FOR EACH ROW
EXECUTE FUNCTION projectpulse093_resync_history_after_project_status();

-- Backfill every latest active Module 055C assignment, including historical
-- rows whose task_id_text contains a task code rather than a UUID.
WITH resolved_history AS (
    SELECT
        history.work_register_task_assignment_history_id,
        history.project_id,
        resolved.task_id,
        history.assigned_user_id,
        history.changed_by_user_id,
        COALESCE(history.effective_start_date, CURRENT_DATE) AS effective_start_date,
        history.effective_end_date,
        NULLIF(history.allocation_percent, 0) AS allocation_percent,
        COALESCE(history.allocated_hours, 0) AS assigned_hours,
        COALESCE(
            NULLIF(history.change_reason, ''),
            'Backfilled from Module 055C task assignment history by Migration 093.'
        ) AS assignment_notes,
        history.created_at
    FROM work_register_task_assignment_history history
    CROSS JOIN LATERAL (
        SELECT projectpulse093_resolve_project_task_id(
            history.project_id,
            history.task_id_text
        ) AS task_id
    ) resolved
    JOIN project_tasks task
      ON task.project_id = history.project_id
     AND task.task_id = resolved.task_id
     AND task.is_active = TRUE
    JOIN projects project
      ON project.project_id = history.project_id
    JOIN app_users engineer
      ON engineer.user_id = history.assigned_user_id
     AND engineer.is_active = TRUE
    WHERE history.assigned_user_id IS NOT NULL
      AND resolved.task_id IS NOT NULL
      AND lower(btrim(COALESCE(history.assignment_status, ''))) = 'active'
      AND (
            history.effective_end_date IS NULL
            OR history.effective_end_date >= CURRENT_DATE
      )
      AND lower(btrim(COALESCE(project.status, 'active'))) NOT IN (
            'closed',
            'complete',
            'completed',
            'done',
            'cancelled',
            'canceled',
            'archived'
      )
),
latest_active_assignments AS (
    SELECT DISTINCT ON (project_id, task_id, assigned_user_id)
        project_id,
        task_id,
        assigned_user_id,
        changed_by_user_id,
        effective_start_date,
        effective_end_date,
        allocation_percent,
        assigned_hours,
        assignment_notes
    FROM resolved_history
    ORDER BY
        project_id,
        task_id,
        assigned_user_id,
        created_at DESC,
        work_register_task_assignment_history_id DESC
)
INSERT INTO project_assignments (
    project_id,
    task_id,
    user_id,
    assigned_by_user_id,
    effective_start_date,
    effective_end_date,
    allocation_percent,
    assigned_hours,
    assignment_source,
    assignment_notes,
    updated_at
)
SELECT
    source.project_id,
    source.task_id,
    source.assigned_user_id,
    source.changed_by_user_id,
    source.effective_start_date,
    source.effective_end_date,
    source.allocation_percent,
    source.assigned_hours,
    'work_register_assignment_history_v2',
    source.assignment_notes,
    NOW()
FROM latest_active_assignments source
ON CONFLICT (project_id, task_id, user_id, effective_start_date)
DO UPDATE SET
    assigned_by_user_id = COALESCE(
        EXCLUDED.assigned_by_user_id,
        project_assignments.assigned_by_user_id
    ),
    effective_end_date = EXCLUDED.effective_end_date,
    allocation_percent = EXCLUDED.allocation_percent,
    assigned_hours = EXCLUDED.assigned_hours,
    assignment_source = EXCLUDED.assignment_source,
    assignment_notes = EXCLUDED.assignment_notes,
    updated_at = NOW();

-- Close only rows owned by the Work Register bridge when no corresponding
-- active history row remains. Manually administered project assignments are
-- intentionally left unchanged.
UPDATE project_assignments assignment
SET effective_end_date = CURRENT_DATE - 1,
    assignment_notes = 'Closed because no active Module 055C assignment remains.',
    updated_at = NOW()
WHERE assignment.assignment_source IN (
        'work_register_assignment_history',
        'work_register_assignment_history_v2'
    )
  AND (
        assignment.effective_end_date IS NULL
        OR assignment.effective_end_date >= CURRENT_DATE
  )
  AND NOT EXISTS (
      SELECT 1
      FROM work_register_task_assignment_history history
      WHERE history.project_id = assignment.project_id
        AND history.assigned_user_id = assignment.user_id
        AND lower(btrim(COALESCE(history.assignment_status, ''))) = 'active'
        AND (
            history.effective_end_date IS NULL
            OR history.effective_end_date >= CURRENT_DATE
        )
        AND projectpulse093_resolve_project_task_id(
              history.project_id,
              history.task_id_text
            ) = assignment.task_id
  );

CREATE INDEX IF NOT EXISTS ix_project_assignments_module001_001a_019_visibility
    ON project_assignments(
        user_id,
        effective_start_date,
        effective_end_date,
        project_id,
        task_id
    );

CREATE INDEX IF NOT EXISTS ix_work_register_assignment_history_active_engineer
    ON work_register_task_assignment_history(
        assigned_user_id,
        assignment_status,
        effective_start_date,
        effective_end_date,
        project_id
    );

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '093_assigned_work_canonical_visibility_repair',
    'Restore canonical owner-catalog rows for Modules 031, 032, and 033 and resolve UUID and durable task-code assignments from Module 055C into canonical project assignments for Modules 019, 001A, and 001, with lifecycle resynchronization',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
