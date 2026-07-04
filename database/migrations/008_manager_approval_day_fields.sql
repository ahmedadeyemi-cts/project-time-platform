-- Project Health Dashboard
-- Migration: 008_manager_approval_day_fields.sql
-- Purpose: Add manager decision metadata to daily timesheet status records.

BEGIN;

ALTER TABLE timesheet_day_statuses
    ADD COLUMN IF NOT EXISTS manager_user_id UUID REFERENCES app_users(user_id),
    ADD COLUMN IF NOT EXISTS manager_decision_comment TEXT,
    ADD COLUMN IF NOT EXISTS manager_approved_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS manager_declined_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS manager_unlocked_at TIMESTAMPTZ;

CREATE INDEX IF NOT EXISTS idx_timesheet_day_status_manager_user
    ON timesheet_day_statuses(manager_user_id);

INSERT INTO schema_migrations (migration_id, description)
VALUES ('008_manager_approval_day_fields', 'Add manager decision metadata to daily timesheet status records')
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
