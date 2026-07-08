-- 055C.2 - Work Register edit audit sidecar
-- No ALTER to existing production project tables.

BEGIN;

CREATE TABLE IF NOT EXISTS work_register_change_history (
    work_register_change_history_id uuid PRIMARY KEY,
    source_table varchar(120) NOT NULL,
    work_id uuid NOT NULL,
    action varchar(120) NOT NULL,
    change_summary text NOT NULL DEFAULT '',
    changed_fields_csv text NOT NULL DEFAULT '',
    changed_by_user_id uuid NULL,
    old_value_json jsonb NULL,
    new_value_json jsonb NULL,
    changed_at timestamp with time zone NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_work_register_change_history_work
    ON work_register_change_history(source_table, work_id);

CREATE INDEX IF NOT EXISTS idx_work_register_change_history_changed_at
    ON work_register_change_history(changed_at DESC);

COMMIT;
