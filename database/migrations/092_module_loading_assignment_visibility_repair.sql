-- Pulse migration 092
-- Repair optional Module Management ownership storage and make Work Register
-- task assignments immediately authoritative for Modules 019, 001A, and 001.

BEGIN;

DO $projectpulse092_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.app_users') IS NULL
       OR to_regclass('public.projects') IS NULL
       OR to_regclass('public.project_tasks') IS NULL
       OR to_regclass('public.project_assignments') IS NULL
       OR to_regclass('public.work_register_task_assignment_history') IS NULL
       OR to_regclass('public.scoped_role_policy_modules') IS NULL THEN
        RAISE EXCEPTION 'Migration 092 requires the module catalog, project assignment, and Work Register foundations.';
    END IF;
END;
$projectpulse092_prerequisites$;

-- Ownership is optional accountability metadata. Repairing its storage must not
-- depend on one environment-specific email identity being present.
ALTER TABLE scoped_role_policy_modules
    ADD COLUMN IF NOT EXISTS owner_user_id UUID NULL,
    ADD COLUMN IF NOT EXISTS owner_revision_number INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS owner_updated_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS owner_updated_by_user_id UUID NULL;

DO $projectpulse092_owner_constraints$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_scoped_role_policy_modules_owner_user'
          AND conrelid = 'public.scoped_role_policy_modules'::regclass
    ) THEN
        ALTER TABLE scoped_role_policy_modules
            ADD CONSTRAINT fk_scoped_role_policy_modules_owner_user
            FOREIGN KEY (owner_user_id) REFERENCES app_users(user_id) ON DELETE SET NULL;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_scoped_role_policy_modules_owner_updated_by'
          AND conrelid = 'public.scoped_role_policy_modules'::regclass
    ) THEN
        ALTER TABLE scoped_role_policy_modules
            ADD CONSTRAINT fk_scoped_role_policy_modules_owner_updated_by
            FOREIGN KEY (owner_updated_by_user_id) REFERENCES app_users(user_id) ON DELETE SET NULL;
    END IF;
END;
$projectpulse092_owner_constraints$;

CREATE INDEX IF NOT EXISTS ix_scoped_role_policy_modules_owner
    ON scoped_role_policy_modules (owner_user_id, is_active, module_code);

ALTER TABLE project_assignments
    ADD COLUMN IF NOT EXISTS assigned_hours NUMERIC(10,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS assignment_source TEXT NOT NULL DEFAULT 'project_assignments',
    ADD COLUMN IF NOT EXISTS assignment_notes TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW();

CREATE OR REPLACE FUNCTION projectpulse092_sync_work_register_assignment()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse092_sync$
DECLARE
    v_task_id UUID;
    v_effective_start DATE;
BEGIN
    IF NEW.task_id_text IS NULL
       OR NEW.task_id_text !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' THEN
        RETURN NEW;
    END IF;

    v_task_id := NEW.task_id_text::uuid;
    v_effective_start := COALESCE(NEW.effective_start_date, CURRENT_DATE);

    IF NOT EXISTS (
        SELECT 1
        FROM project_tasks task
        WHERE task.task_id = v_task_id
          AND task.project_id = NEW.project_id
    ) THEN
        RETURN NEW;
    END IF;

    IF TG_OP = 'UPDATE'
       AND OLD.assigned_user_id IS NOT NULL
       AND (
            OLD.assigned_user_id IS DISTINCT FROM NEW.assigned_user_id
            OR COALESCE(NEW.assignment_status, '') <> 'active'
            OR (NEW.effective_end_date IS NOT NULL AND NEW.effective_end_date < CURRENT_DATE)
       ) THEN
        UPDATE project_assignments
        SET effective_end_date = COALESCE(NEW.effective_end_date, CURRENT_DATE - 1),
            assignment_source = 'work_register_assignment_history',
            assignment_notes = COALESCE(NULLIF(NEW.change_reason, ''), 'Assignment closed from Module 055C task assignment history.'),
            updated_at = NOW()
        WHERE project_id = NEW.project_id
          AND task_id = v_task_id
          AND user_id = OLD.assigned_user_id
          AND (effective_end_date IS NULL OR effective_end_date >= CURRENT_DATE);
    END IF;

    IF NEW.assigned_user_id IS NULL THEN
        RETURN NEW;
    END IF;

    IF COALESCE(NEW.assignment_status, '') = 'active'
       AND (NEW.effective_end_date IS NULL OR NEW.effective_end_date >= CURRENT_DATE) THEN
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
            v_task_id,
            NEW.assigned_user_id,
            NEW.changed_by_user_id,
            v_effective_start,
            NEW.effective_end_date,
            NULLIF(NEW.allocation_percent, 0),
            COALESCE(NEW.allocated_hours, 0),
            'work_register_assignment_history',
            COALESCE(NULLIF(NEW.change_reason, ''), 'Synchronized from Module 055C task assignment history.'),
            NOW()
        )
        ON CONFLICT (project_id, task_id, user_id, effective_start_date)
        DO UPDATE SET
            assigned_by_user_id = COALESCE(EXCLUDED.assigned_by_user_id, project_assignments.assigned_by_user_id),
            effective_end_date = EXCLUDED.effective_end_date,
            allocation_percent = EXCLUDED.allocation_percent,
            assigned_hours = EXCLUDED.assigned_hours,
            assignment_source = EXCLUDED.assignment_source,
            assignment_notes = EXCLUDED.assignment_notes,
            updated_at = NOW();
    ELSE
        UPDATE project_assignments
        SET effective_end_date = COALESCE(NEW.effective_end_date, CURRENT_DATE - 1),
            assignment_source = 'work_register_assignment_history',
            assignment_notes = COALESCE(NULLIF(NEW.change_reason, ''), 'Assignment closed from Module 055C task assignment history.'),
            updated_at = NOW()
        WHERE project_id = NEW.project_id
          AND task_id = v_task_id
          AND user_id = NEW.assigned_user_id
          AND (effective_end_date IS NULL OR effective_end_date >= CURRENT_DATE);
    END IF;

    RETURN NEW;
END;
$projectpulse092_sync$;

DROP TRIGGER IF EXISTS trg_projectpulse092_sync_work_register_assignment
    ON work_register_task_assignment_history;

CREATE TRIGGER trg_projectpulse092_sync_work_register_assignment
AFTER INSERT OR UPDATE OF
    assigned_user_id,
    assignment_status,
    effective_start_date,
    effective_end_date,
    allocated_hours,
    allocation_percent,
    change_reason
ON work_register_task_assignment_history
FOR EACH ROW
EXECUTE FUNCTION projectpulse092_sync_work_register_assignment();

-- Backfill the latest currently active sidecar assignment for every valid
-- project/task/Engineer identity. This repairs existing Service Request,
-- Pre-Sales, and Internal assignments without creating a second task model.
WITH latest_active_assignments AS (
    SELECT DISTINCT ON (history.project_id, history.task_id_text, history.assigned_user_id)
        history.project_id,
        history.task_id_text::uuid AS task_id,
        history.assigned_user_id,
        history.changed_by_user_id,
        COALESCE(history.effective_start_date, CURRENT_DATE) AS effective_start_date,
        history.effective_end_date,
        NULLIF(history.allocation_percent, 0) AS allocation_percent,
        COALESCE(history.allocated_hours, 0) AS assigned_hours,
        COALESCE(NULLIF(history.change_reason, ''), 'Backfilled from Module 055C task assignment history by Migration 092.') AS assignment_notes
    FROM work_register_task_assignment_history history
    JOIN project_tasks task
      ON task.project_id = history.project_id
     AND history.task_id_text ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
     AND task.task_id = history.task_id_text::uuid
    JOIN projects project
      ON project.project_id = history.project_id
    JOIN app_users engineer
      ON engineer.user_id = history.assigned_user_id
     AND engineer.is_active = TRUE
    WHERE history.assigned_user_id IS NOT NULL
      AND history.assignment_status = 'active'
      AND (history.effective_end_date IS NULL OR history.effective_end_date >= CURRENT_DATE)
      AND COALESCE(project.status, 'active') NOT IN ('closed', 'completed', 'cancelled', 'canceled', 'archived')
      AND task.is_active = TRUE
    ORDER BY history.project_id, history.task_id_text, history.assigned_user_id, history.created_at DESC
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
    'work_register_assignment_history',
    source.assignment_notes,
    NOW()
FROM latest_active_assignments source
ON CONFLICT (project_id, task_id, user_id, effective_start_date)
DO UPDATE SET
    assigned_by_user_id = COALESCE(EXCLUDED.assigned_by_user_id, project_assignments.assigned_by_user_id),
    effective_end_date = EXCLUDED.effective_end_date,
    allocation_percent = EXCLUDED.allocation_percent,
    assigned_hours = EXCLUDED.assigned_hours,
    assignment_source = EXCLUDED.assignment_source,
    assignment_notes = EXCLUDED.assignment_notes,
    updated_at = NOW();

-- End only bridge-owned canonical rows that no longer have an active sidecar
-- assignment. Manually managed canonical assignments are never changed here.
UPDATE project_assignments assignment
SET effective_end_date = CURRENT_DATE - 1,
    assignment_notes = 'Closed because no active Module 055C assignment remains.',
    updated_at = NOW()
WHERE assignment.assignment_source IN (
        'work_register_assignment_history',
        'work_register_intake_final_save'
    )
  AND (assignment.effective_end_date IS NULL OR assignment.effective_end_date >= CURRENT_DATE)
  AND NOT EXISTS (
      SELECT 1
      FROM work_register_task_assignment_history history
      WHERE history.project_id = assignment.project_id
        AND history.task_id_text = assignment.task_id::text
        AND history.assigned_user_id = assignment.user_id
        AND history.assignment_status = 'active'
        AND (history.effective_end_date IS NULL OR history.effective_end_date >= CURRENT_DATE)
  );

CREATE INDEX IF NOT EXISTS ix_project_assignments_work_register_visibility
    ON project_assignments (user_id, effective_start_date, effective_end_date, project_id, task_id)
    WHERE assignment_source IN ('work_register_assignment_history', 'work_register_intake_final_save');

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '092_module_loading_assignment_visibility_repair',
    'Repair module owner storage and synchronize Work Register assignments into canonical project assignments',
    NOW()
)
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
