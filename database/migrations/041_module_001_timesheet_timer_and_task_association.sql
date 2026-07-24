-- ProjectPulse Module 001 Timesheet timer and task association foundation.
-- Additive after migration 040. Existing Timesheet records and identifiers remain intact.
BEGIN;

DO $module001_041_prerequisite$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id = '040_scoped_role_policy_versions'
    ) THEN
        RAISE EXCEPTION 'Migration 041 requires 040_scoped_role_policy_versions first.';
    END IF;
END;
$module001_041_prerequisite$;

ALTER TABLE timesheets
    ADD COLUMN IF NOT EXISTS submitted_by_user_id UUID NULL REFERENCES app_users(user_id),
    ADD COLUMN IF NOT EXISTS submission_reason TEXT NULL;

CREATE TABLE IF NOT EXISTS module001_weekly_task_lines (
    weekly_task_line_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(user_id),
    week_start_date DATE NOT NULL,
    customer_id UUID NULL REFERENCES clients(client_id),
    project_id UUID NULL REFERENCES projects(project_id),
    task_id UUID NULL REFERENCES project_tasks(task_id),
    work_item_id UUID NULL,
    assignment_id UUID NULL REFERENCES project_assignments(project_assignment_id),
    non_project_time_category_id UUID NULL REFERENCES non_project_time_categories(non_project_time_category_id),
    activity_type VARCHAR(50) NOT NULL,
    line_source VARCHAR(50) NOT NULL DEFAULT 'WORK_QUEUE',
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id),
    updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id),
    CONSTRAINT chk_module001_weekly_line_activity
        CHECK (activity_type IN ('PROJECT_TASK','NON_PROJECT')),
    CONSTRAINT chk_module001_weekly_line_source
        CHECK (line_source IN ('WORK_QUEUE','TIMER','CALENDAR','EXISTING_ENTRY')),
    CONSTRAINT chk_module001_weekly_line_target
        CHECK (
            (activity_type = 'PROJECT_TASK' AND project_id IS NOT NULL AND task_id IS NOT NULL AND assignment_id IS NOT NULL AND non_project_time_category_id IS NULL)
            OR
            (activity_type = 'NON_PROJECT' AND project_id IS NULL AND task_id IS NULL AND assignment_id IS NULL AND non_project_time_category_id IS NOT NULL)
        )
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_module001_weekly_project_line
    ON module001_weekly_task_lines(user_id, week_start_date, assignment_id)
    WHERE assignment_id IS NOT NULL AND is_active = TRUE;
CREATE UNIQUE INDEX IF NOT EXISTS ux_module001_weekly_non_project_line
    ON module001_weekly_task_lines(user_id, week_start_date, non_project_time_category_id)
    WHERE non_project_time_category_id IS NOT NULL AND is_active = TRUE;

CREATE TABLE IF NOT EXISTS module001_timer_sessions (
    timer_session_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(user_id),
    week_start_date DATE NOT NULL,
    entry_date DATE NOT NULL,
    customer_id UUID NULL REFERENCES clients(client_id),
    project_id UUID NULL REFERENCES projects(project_id),
    task_id UUID NULL REFERENCES project_tasks(task_id),
    work_item_id UUID NULL,
    assignment_id UUID NULL REFERENCES project_assignments(project_assignment_id),
    non_project_time_category_id UUID NULL REFERENCES non_project_time_categories(non_project_time_category_id),
    time_classification VARCHAR(50) NOT NULL DEFAULT 'normal',
    time_zone_id TEXT NOT NULL DEFAULT 'UTC',
    started_at_utc TIMESTAMPTZ NOT NULL,
    stopped_at_utc TIMESTAMPTZ NULL,
    effective_stopped_at_utc TIMESTAMPTZ NULL,
    actual_elapsed_seconds INTEGER NULL,
    rounded_minutes INTEGER NULL,
    description TEXT NULL,
    timer_status VARCHAR(50) NOT NULL DEFAULT 'RUNNING',
    auto_stopped BOOLEAN NOT NULL DEFAULT FALSE,
    resulting_timesheet_entry_id UUID NULL REFERENCES time_entries(time_entry_id),
    row_version INTEGER NOT NULL DEFAULT 1,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id),
    updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id),
    CONSTRAINT chk_module001_timer_classification CHECK (time_classification IN ('normal','afterhours')),
    CONSTRAINT chk_module001_timer_status CHECK (timer_status IN ('RUNNING','STOPPED_DRAFT','AUTO_STOPPED','DISCARDED','CONVERTED_TO_ENTRY')),
    CONSTRAINT chk_module001_timer_target CHECK (
        (project_id IS NOT NULL AND task_id IS NOT NULL AND assignment_id IS NOT NULL AND non_project_time_category_id IS NULL)
        OR
        (project_id IS NULL AND task_id IS NULL AND assignment_id IS NULL AND non_project_time_category_id IS NOT NULL)
    ),
    CONSTRAINT chk_module001_timer_stop_order CHECK (effective_stopped_at_utc IS NULL OR effective_stopped_at_utc >= started_at_utc),
    CONSTRAINT chk_module001_timer_actual_seconds CHECK (actual_elapsed_seconds IS NULL OR actual_elapsed_seconds BETWEEN 0 AND 43200),
    CONSTRAINT chk_module001_timer_rounded_minutes CHECK (rounded_minutes IS NULL OR (rounded_minutes BETWEEN 0 AND 720 AND rounded_minutes % 15 = 0)),
    CONSTRAINT chk_module001_timer_running_shape CHECK (
        timer_status <> 'RUNNING'
        OR (stopped_at_utc IS NULL AND effective_stopped_at_utc IS NULL AND actual_elapsed_seconds IS NULL AND rounded_minutes IS NULL)
    )
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_module001_one_running_timer_per_user
    ON module001_timer_sessions(user_id)
    WHERE timer_status = 'RUNNING';
CREATE INDEX IF NOT EXISTS ix_module001_timer_user_week
    ON module001_timer_sessions(user_id, week_start_date, started_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_module001_timer_assignment
    ON module001_timer_sessions(assignment_id, started_at_utc DESC)
    WHERE assignment_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS module001_timer_daily_segments (
    timer_daily_segment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    timer_session_id UUID NOT NULL REFERENCES module001_timer_sessions(timer_session_id) ON DELETE RESTRICT,
    local_entry_date DATE NOT NULL,
    actual_elapsed_seconds INTEGER NOT NULL,
    allocated_rounded_minutes INTEGER NOT NULL,
    resulting_timesheet_entry_id UUID NULL REFERENCES time_entries(time_entry_id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_module001_timer_daily_segment UNIQUE (timer_session_id, local_entry_date),
    CONSTRAINT chk_module001_timer_segment_actual CHECK (actual_elapsed_seconds >= 0),
    CONSTRAINT chk_module001_timer_segment_rounded CHECK (allocated_rounded_minutes BETWEEN 0 AND 720 AND allocated_rounded_minutes % 15 = 0)
);

CREATE TABLE IF NOT EXISTS module001_timesheet_entry_associations (
    time_entry_id UUID PRIMARY KEY REFERENCES time_entries(time_entry_id) ON DELETE CASCADE,
    customer_id UUID NULL REFERENCES clients(client_id),
    project_id UUID NULL REFERENCES projects(project_id),
    task_id UUID NULL REFERENCES project_tasks(task_id),
    work_item_id UUID NULL,
    assignment_id UUID NULL REFERENCES project_assignments(project_assignment_id),
    non_project_time_category_id UUID NULL REFERENCES non_project_time_categories(non_project_time_category_id),
    source_timer_session_id UUID NULL REFERENCES module001_timer_sessions(timer_session_id),
    association_source VARCHAR(50) NOT NULL DEFAULT 'EXISTING_ENTRY',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id),
    updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id),
    CONSTRAINT chk_module001_association_source CHECK (association_source IN ('EXISTING_ENTRY','WORK_QUEUE','TIMER','CALENDAR')),
    CONSTRAINT chk_module001_association_target CHECK (
        (project_id IS NOT NULL AND task_id IS NOT NULL AND assignment_id IS NOT NULL AND non_project_time_category_id IS NULL)
        OR
        (project_id IS NULL AND task_id IS NULL AND assignment_id IS NULL AND non_project_time_category_id IS NOT NULL)
    )
);

CREATE INDEX IF NOT EXISTS ix_module001_entry_association_assignment
    ON module001_timesheet_entry_associations(assignment_id)
    WHERE assignment_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS module001_timer_audit_events (
    timer_audit_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    timer_session_id UUID NOT NULL REFERENCES module001_timer_sessions(timer_session_id) ON DELETE RESTRICT,
    actor_user_id UUID NOT NULL REFERENCES app_users(user_id),
    event_code VARCHAR(80) NOT NULL,
    reason TEXT NOT NULL DEFAULT '',
    previous_state JSONB NOT NULL DEFAULT '{}'::jsonb,
    new_state JSONB NOT NULL DEFAULT '{}'::jsonb,
    event_metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_module001_timer_audit_event CHECK (
        event_code IN (
            'TIMER_STARTED','TIMER_STOPPED','TIMER_AUTO_STOPPED','TIMER_DISCARDED',
            'TASK_ASSOCIATED','TASK_CHANGED','DESCRIPTION_CHANGED','DRAFT_CREATED',
            'SUBMISSION_VALIDATION_FAILED','TIMESHEET_SUBMITTED'
        )
    )
);

CREATE OR REPLACE FUNCTION module001_041_touch_timer()
RETURNS trigger LANGUAGE plpgsql AS $module001_041_touch_timer_body$
BEGIN
    NEW.updated_at = NOW();
    NEW.row_version = OLD.row_version + 1;
    RETURN NEW;
END;
$module001_041_touch_timer_body$;

DROP TRIGGER IF EXISTS trg_module001_041_touch_timer ON module001_timer_sessions;
CREATE TRIGGER trg_module001_041_touch_timer
BEFORE UPDATE ON module001_timer_sessions
FOR EACH ROW EXECUTE FUNCTION module001_041_touch_timer();

CREATE OR REPLACE FUNCTION module001_041_touch_weekly_line()
RETURNS trigger LANGUAGE plpgsql AS $module001_041_touch_weekly_line_body$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$module001_041_touch_weekly_line_body$;

DROP TRIGGER IF EXISTS trg_module001_041_touch_weekly_line ON module001_weekly_task_lines;
CREATE TRIGGER trg_module001_041_touch_weekly_line
BEFORE UPDATE ON module001_weekly_task_lines
FOR EACH ROW EXECUTE FUNCTION module001_041_touch_weekly_line();

CREATE OR REPLACE FUNCTION module001_041_touch_association()
RETURNS trigger LANGUAGE plpgsql AS $module001_041_touch_association_body$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$module001_041_touch_association_body$;

DROP TRIGGER IF EXISTS trg_module001_041_touch_association ON module001_timesheet_entry_associations;
CREATE TRIGGER trg_module001_041_touch_association
BEFORE UPDATE ON module001_timesheet_entry_associations
FOR EACH ROW EXECUTE FUNCTION module001_041_touch_association();

CREATE OR REPLACE FUNCTION module001_041_block_audit_mutation()
RETURNS trigger LANGUAGE plpgsql AS $module001_041_immutable_body$
BEGIN
    RAISE EXCEPTION 'Module 001 timer audit evidence is immutable.';
END;
$module001_041_immutable_body$;

DROP TRIGGER IF EXISTS trg_module001_041_timer_audit_immutable ON module001_timer_audit_events;
CREATE TRIGGER trg_module001_041_timer_audit_immutable
BEFORE UPDATE OR DELETE ON module001_timer_audit_events
FOR EACH ROW EXECUTE FUNCTION module001_041_block_audit_mutation();

UPDATE scoped_role_policy_modules
SET module_name = 'Timesheet',
    permission_notes = CASE
        WHEN permission_notes ILIKE '%Timesheet%' THEN permission_notes
        ELSE CONCAT_WS(' ', NULLIF(permission_notes, ''), 'User-facing Module 001 name is Timesheet; technical Time Entry identifiers remain compatible.')
    END
WHERE module_code = '001';

GRANT SELECT, INSERT, UPDATE ON TABLE module001_weekly_task_lines TO "ptp_app";
GRANT SELECT, INSERT, UPDATE ON TABLE module001_timer_sessions TO "ptp_app";
GRANT SELECT, INSERT, UPDATE ON TABLE module001_timer_daily_segments TO "ptp_app";
GRANT SELECT, INSERT, UPDATE ON TABLE module001_timesheet_entry_associations TO "ptp_app";
GRANT SELECT, INSERT ON TABLE module001_timer_audit_events TO "ptp_app";

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '041_module_001_timesheet_timer_and_task_association',
    'Add server-authoritative Module 001 timers, weekly task lines, durable task associations, and Timesheet submission attribution',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
