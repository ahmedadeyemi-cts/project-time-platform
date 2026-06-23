-- Project Time Platform
-- Migration: 005_utilization_policy_model.sql
-- Purpose: Add utilization policy, target thresholds, weekly summaries, and utilization buckets.

BEGIN;

CREATE TABLE IF NOT EXISTS utilization_policies (
    utilization_policy_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    policy_name VARCHAR(255) NOT NULL UNIQUE,
    period_type VARCHAR(50) NOT NULL DEFAULT 'quarterly',
    standard_period_hours NUMERIC(8,2) NOT NULL DEFAULT 482.00,
    default_target_percent NUMERIC(5,2) NOT NULL DEFAULT 70.00,
    presales_training_requires_approval BOOLEAN NOT NULL DEFAULT TRUE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_utilization_policy_period_type CHECK (period_type IN ('monthly', 'quarterly', 'annual')),
    CONSTRAINT chk_utilization_policy_standard_hours CHECK (standard_period_hours > 0),
    CONSTRAINT chk_utilization_policy_target CHECK (default_target_percent > 0)
);

CREATE TABLE IF NOT EXISTS utilization_policy_targets (
    utilization_policy_target_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    utilization_policy_id UUID NOT NULL REFERENCES utilization_policies(utilization_policy_id) ON DELETE CASCADE,
    target_percent NUMERIC(5,2) NOT NULL,
    target_hours NUMERIC(8,2) NOT NULL,
    bonus_reference_amount NUMERIC(12,2),
    display_order INTEGER NOT NULL DEFAULT 100,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_utilization_policy_target UNIQUE (utilization_policy_id, target_percent),
    CONSTRAINT chk_utilization_policy_target_percent CHECK (target_percent > 0),
    CONSTRAINT chk_utilization_policy_target_hours CHECK (target_hours >= 0),
    CONSTRAINT chk_utilization_policy_bonus_reference CHECK (bonus_reference_amount IS NULL OR bonus_reference_amount >= 0)
);

ALTER TABLE project_tasks
    ADD COLUMN IF NOT EXISTS utilization_bucket VARCHAR(50) NOT NULL DEFAULT 'billable',
    ADD COLUMN IF NOT EXISTS utilization_requires_approval BOOLEAN NOT NULL DEFAULT TRUE;

DO $$
BEGIN
    ALTER TABLE project_tasks
        ADD CONSTRAINT chk_project_task_utilization_bucket
        CHECK (utilization_bucket IN ('billable', 'presales_training', 'non_billable', 'excluded'));
EXCEPTION
    WHEN duplicate_object THEN NULL;
END $$;

ALTER TABLE non_project_time_categories
    ADD COLUMN IF NOT EXISTS utilization_bucket VARCHAR(50) NOT NULL DEFAULT 'excluded';

DO $$
BEGIN
    ALTER TABLE non_project_time_categories
        ADD CONSTRAINT chk_non_project_time_utilization_bucket
        CHECK (utilization_bucket IN ('pto', 'presales_training', 'non_billable', 'excluded'));
EXCEPTION
    WHEN duplicate_object THEN NULL;
END $$;

UPDATE non_project_time_categories
SET utilization_bucket = 'pto', updated_at = NOW()
WHERE category_code IN ('HOLIDAY', 'PERSONAL_HOLIDAY', 'SICK_LEAVE', 'VACATION');

UPDATE non_project_time_categories
SET utilization_bucket = 'presales_training', updated_at = NOW()
WHERE category_code = 'TRAINING';

UPDATE non_project_time_categories
SET utilization_bucket = 'non_billable', updated_at = NOW()
WHERE category_code IN ('ADMINISTRATIVE', 'PEER_SUPPORT', 'VOLUNTEER_TIME');

ALTER TABLE utilization_snapshots
    ADD COLUMN IF NOT EXISTS standard_period_hours NUMERIC(8,2) NOT NULL DEFAULT 482.00,
    ADD COLUMN IF NOT EXISTS regular_utilized_hours NUMERIC(8,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS afterhours_utilized_hours NUMERIC(8,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS pto_hours NUMERIC(8,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS presales_training_hours NUMERIC(8,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS total_utilization_hours NUMERIC(8,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS utilization_percent_without_presales NUMERIC(6,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS total_utilization_percent NUMERIC(6,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS bonus_reference_amount NUMERIC(12,2);

CREATE TABLE IF NOT EXISTS utilization_weekly_summaries (
    utilization_weekly_summary_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    week_start_date DATE NOT NULL,
    week_end_date DATE NOT NULL,
    regular_utilized_hours NUMERIC(8,2) NOT NULL DEFAULT 0,
    afterhours_utilized_hours NUMERIC(8,2) NOT NULL DEFAULT 0,
    pto_hours NUMERIC(8,2) NOT NULL DEFAULT 0,
    presales_training_hours NUMERIC(8,2) NOT NULL DEFAULT 0,
    total_hours NUMERIC(8,2) NOT NULL DEFAULT 0,
    standard_period_hours NUMERIC(8,2) NOT NULL DEFAULT 482.00,
    utilization_percent_without_presales NUMERIC(6,2) NOT NULL DEFAULT 0,
    total_utilization_percent NUMERIC(6,2) NOT NULL DEFAULT 0,
    calculation_basis VARCHAR(50) NOT NULL DEFAULT 'approved',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_utilization_weekly_user_week UNIQUE (user_id, week_start_date),
    CONSTRAINT chk_utilization_weekly_dates CHECK (week_end_date >= week_start_date),
    CONSTRAINT chk_utilization_weekly_hours CHECK (
        regular_utilized_hours >= 0
        AND afterhours_utilized_hours >= 0
        AND pto_hours >= 0
        AND presales_training_hours >= 0
        AND total_hours >= 0
        AND standard_period_hours > 0
    ),
    CONSTRAINT chk_utilization_weekly_basis CHECK (calculation_basis IN ('submitted', 'approved', 'reconciled'))
);

DROP TRIGGER IF EXISTS trg_utilization_policies_updated_at ON utilization_policies;
CREATE TRIGGER trg_utilization_policies_updated_at
    BEFORE UPDATE ON utilization_policies
    FOR EACH ROW
    EXECUTE FUNCTION set_updated_at();

DROP TRIGGER IF EXISTS trg_utilization_policy_targets_updated_at ON utilization_policy_targets;
CREATE TRIGGER trg_utilization_policy_targets_updated_at
    BEFORE UPDATE ON utilization_policy_targets
    FOR EACH ROW
    EXECUTE FUNCTION set_updated_at();

DROP TRIGGER IF EXISTS trg_utilization_weekly_summaries_updated_at ON utilization_weekly_summaries;
CREATE TRIGGER trg_utilization_weekly_summaries_updated_at
    BEFORE UPDATE ON utilization_weekly_summaries
    FOR EACH ROW
    EXECUTE FUNCTION set_updated_at();

CREATE INDEX IF NOT EXISTS idx_project_tasks_utilization_bucket ON project_tasks(utilization_bucket);
CREATE INDEX IF NOT EXISTS idx_non_project_time_categories_utilization_bucket ON non_project_time_categories(utilization_bucket);
CREATE INDEX IF NOT EXISTS idx_utilization_policies_active ON utilization_policies(is_active);
CREATE INDEX IF NOT EXISTS idx_utilization_policy_targets_policy ON utilization_policy_targets(utilization_policy_id);
CREATE INDEX IF NOT EXISTS idx_utilization_weekly_summaries_user_week ON utilization_weekly_summaries(user_id, week_start_date, week_end_date);

INSERT INTO utilization_policies (
    policy_name,
    period_type,
    standard_period_hours,
    default_target_percent,
    presales_training_requires_approval,
    is_active
)
VALUES ('Default 2026 Quarterly Utilization Policy', 'quarterly', 482.00, 70.00, TRUE, TRUE)
ON CONFLICT (policy_name) DO UPDATE SET
    period_type = EXCLUDED.period_type,
    standard_period_hours = EXCLUDED.standard_period_hours,
    default_target_percent = EXCLUDED.default_target_percent,
    presales_training_requires_approval = EXCLUDED.presales_training_requires_approval,
    is_active = TRUE,
    updated_at = NOW();

WITH policy AS (
    SELECT utilization_policy_id
    FROM utilization_policies
    WHERE policy_name = 'Default 2026 Quarterly Utilization Policy'
)
INSERT INTO utilization_policy_targets (
    utilization_policy_id,
    target_percent,
    target_hours,
    bonus_reference_amount,
    display_order
)
SELECT utilization_policy_id, target_percent, target_hours, bonus_reference_amount, display_order
FROM policy
CROSS JOIN (VALUES
    (70.00, 337.40, 6240.00, 10),
    (75.00, 361.50, 6630.00, 20),
    (80.00, 385.60, 7800.00, 30),
    (85.00, 409.70, 8190.00, 40),
    (90.00, 433.80, 8580.00, 50),
    (95.00, 457.90, 8970.00, 60),
    (100.00, 482.00, 9360.00, 70),
    (105.00, 506.10, 9750.00, 80)
) AS targets(target_percent, target_hours, bonus_reference_amount, display_order)
ON CONFLICT (utilization_policy_id, target_percent) DO UPDATE SET
    target_hours = EXCLUDED.target_hours,
    bonus_reference_amount = EXCLUDED.bonus_reference_amount,
    display_order = EXCLUDED.display_order,
    updated_at = NOW();

INSERT INTO schema_migrations (migration_id, description)
VALUES ('005_utilization_policy_model', 'Add utilization policies, targets, weekly summaries, and utilization buckets')
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
