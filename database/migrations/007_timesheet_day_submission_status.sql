-- Project Pulse
-- Migration: 007_timesheet_day_submission_status.sql
-- Purpose: Track submitted, locked, and unlocked state at the individual work-day level.

BEGIN;

CREATE TABLE IF NOT EXISTS timesheet_day_statuses (
    timesheet_day_status_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    timesheet_id UUID NOT NULL REFERENCES timesheets(timesheet_id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES app_users(user_id),
    work_date DATE NOT NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'draft',
    submitted_at TIMESTAMPTZ,
    unlocked_at TIMESTAMPTZ,
    unlocked_by_user_id UUID REFERENCES app_users(user_id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_timesheet_day_status UNIQUE (timesheet_id, work_date),
    CONSTRAINT chk_timesheet_day_status CHECK (status IN ('draft', 'submitted', 'manager_approved', 'manager_declined', 'locked'))
);

CREATE INDEX IF NOT EXISTS idx_timesheet_day_status_user_date
    ON timesheet_day_statuses(user_id, work_date);

CREATE INDEX IF NOT EXISTS idx_timesheet_day_status_timesheet_status
    ON timesheet_day_statuses(timesheet_id, status);

INSERT INTO schema_migrations (migration_id, description)
VALUES ('007_timesheet_day_submission_status', 'Track submitted and locked state at the individual work-day level')
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
