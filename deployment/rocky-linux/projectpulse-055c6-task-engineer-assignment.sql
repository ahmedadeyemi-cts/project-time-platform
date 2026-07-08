-- 055C.6 - Task-level engineer assignment / reassignment sidecar
-- Preserves assignment history and does not require ALTER on existing project/task/time tables.

BEGIN;

CREATE TABLE IF NOT EXISTS work_register_task_assignment_history (
    work_register_task_assignment_history_id uuid PRIMARY KEY,
    project_id uuid NOT NULL,
    task_id_text text NOT NULL,
    task_name_snapshot text NOT NULL DEFAULT '',
    assigned_user_id uuid NULL,
    previous_assigned_user_id uuid NULL,
    allocated_hours numeric(12,2) NULL,
    billable boolean NULL,
    utilization_eligible boolean NULL,
    assignment_status varchar(40) NOT NULL DEFAULT 'active',
    effective_start_date date NOT NULL DEFAULT CURRENT_DATE,
    effective_end_date date NULL,
    change_reason text NOT NULL DEFAULT '',
    changed_by_user_id uuid NULL,
    old_value_json jsonb NULL,
    new_value_json jsonb NULL,
    created_at timestamp with time zone NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_work_register_task_assignment_project
    ON work_register_task_assignment_history(project_id);

CREATE INDEX IF NOT EXISTS idx_work_register_task_assignment_task
    ON work_register_task_assignment_history(task_id_text);

CREATE INDEX IF NOT EXISTS idx_work_register_task_assignment_active
    ON work_register_task_assignment_history(project_id, task_id_text, assignment_status)
    WHERE assignment_status = 'active';

CREATE INDEX IF NOT EXISTS idx_work_register_task_assignment_changed
    ON work_register_task_assignment_history(created_at DESC);

COMMIT;
