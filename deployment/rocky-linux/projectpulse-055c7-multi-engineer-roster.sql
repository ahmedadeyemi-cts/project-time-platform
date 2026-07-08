-- 055C.7 - Multi-engineer task assignment roster
-- Extends the 055C.6 sidecar table. Existing historical assignments remain preserved.

BEGIN;

ALTER TABLE work_register_task_assignment_history
    ADD COLUMN IF NOT EXISTS allocation_percent numeric(8,2) NULL;

ALTER TABLE work_register_task_assignment_history
    ADD COLUMN IF NOT EXISTS assignment_role varchar(80) NOT NULL DEFAULT 'engineer';

ALTER TABLE work_register_task_assignment_history
    ADD COLUMN IF NOT EXISTS roster_batch_id uuid NULL;

ALTER TABLE work_register_task_assignment_history
    ADD COLUMN IF NOT EXISTS is_primary boolean NOT NULL DEFAULT false;

CREATE INDEX IF NOT EXISTS idx_work_register_task_assignment_roster_batch
    ON work_register_task_assignment_history(roster_batch_id);

CREATE INDEX IF NOT EXISTS idx_work_register_task_assignment_active_user
    ON work_register_task_assignment_history(project_id, task_id_text, assigned_user_id)
    WHERE assignment_status = 'active';

COMMIT;
