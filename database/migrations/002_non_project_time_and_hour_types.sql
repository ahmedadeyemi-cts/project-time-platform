-- Project Time Platform
-- Migration: 002_non_project_time_and_hour_types.sql
-- Purpose: Add non-project time category support and normal/afterhours time entry support.

BEGIN;

CREATE TABLE IF NOT EXISTS non_project_time_categories (
    non_project_time_category_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    category_code VARCHAR(100) NOT NULL UNIQUE,
    category_name VARCHAR(255) NOT NULL,
    category_description TEXT,
    utilization_classification VARCHAR(50) NOT NULL DEFAULT 'non_billable',
    requires_approval BOOLEAN NOT NULL DEFAULT TRUE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    display_order INTEGER NOT NULL DEFAULT 100,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_non_project_utilization_classification CHECK (
        utilization_classification IN ('administrative', 'leave', 'non_billable', 'paid_time_off', 'training', 'unpaid_time_off')
    )
);

ALTER TABLE time_entries
    ADD COLUMN IF NOT EXISTS non_project_time_category_id UUID REFERENCES non_project_time_categories(non_project_time_category_id),
    ADD COLUMN IF NOT EXISTS time_type VARCHAR(50) NOT NULL DEFAULT 'normal';

ALTER TABLE time_entries
    ALTER COLUMN project_id DROP NOT NULL;

DO $$
BEGIN
    ALTER TABLE time_entries
        ADD CONSTRAINT chk_time_entry_time_type
        CHECK (time_type IN ('normal', 'afterhours'));
EXCEPTION
    WHEN duplicate_object THEN NULL;
END $$;

DO $$
BEGIN
    ALTER TABLE time_entries
        ADD CONSTRAINT chk_time_entry_project_or_non_project
        CHECK (
            (project_id IS NOT NULL AND non_project_time_category_id IS NULL)
            OR
            (project_id IS NULL AND non_project_time_category_id IS NOT NULL)
        );
EXCEPTION
    WHEN duplicate_object THEN NULL;
END $$;

DO $$
BEGIN
    ALTER TABLE time_entries
        ADD CONSTRAINT chk_time_entry_task_requires_project
        CHECK (task_id IS NULL OR project_id IS NOT NULL);
EXCEPTION
    WHEN duplicate_object THEN NULL;
END $$;

DROP TRIGGER IF EXISTS trg_non_project_time_categories_updated_at ON non_project_time_categories;
CREATE TRIGGER trg_non_project_time_categories_updated_at
    BEFORE UPDATE ON non_project_time_categories
    FOR EACH ROW
    EXECUTE FUNCTION set_updated_at();

CREATE INDEX IF NOT EXISTS idx_non_project_time_categories_active ON non_project_time_categories(is_active);
CREATE INDEX IF NOT EXISTS idx_non_project_time_categories_code ON non_project_time_categories(category_code);
CREATE INDEX IF NOT EXISTS idx_time_entries_non_project_category ON time_entries(non_project_time_category_id);
CREATE INDEX IF NOT EXISTS idx_time_entries_time_type ON time_entries(time_type);
CREATE INDEX IF NOT EXISTS idx_time_entries_user_work_date_time_type ON time_entries(user_id, work_date, time_type);

INSERT INTO schema_migrations (migration_id, description)
VALUES ('002_non_project_time_and_hour_types', 'Add non-project time categories and normal/afterhours time entry support')
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
