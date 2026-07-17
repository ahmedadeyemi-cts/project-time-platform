-- MODULE 063 - Opportunities & Action Tracker
--
-- Sidecar-safe:
--   * Adds opportunity, task, and event tables only.
--   * Does not modify or fabricate existing customer, project, user, or billing data.
--   * Supports manual ProjectPulse entry and future CRM/XLSX import by external ID.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS opportunities (
    opportunity_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    external_opportunity_id TEXT NOT NULL DEFAULT '',
    source_system TEXT NOT NULL DEFAULT 'projectpulse',

    client_id UUID NULL
        REFERENCES clients(client_id) ON DELETE SET NULL,

    account_name TEXT NOT NULL DEFAULT '',
    topic TEXT NOT NULL,

    owner_user_id UUID NULL
        REFERENCES app_users(user_id) ON DELETE SET NULL,

    opportunity_status TEXT NOT NULL DEFAULT 'active',
    close_outcome TEXT NULL,

    estimated_revenue NUMERIC(14,2) NULL,
    actual_revenue NUMERIC(14,2) NULL,

    active_date DATE NOT NULL DEFAULT CURRENT_DATE,
    closed_date DATE NULL,

    notes TEXT NOT NULL DEFAULT '',

    created_by_user_id UUID NOT NULL
        REFERENCES app_users(user_id),

    updated_by_user_id UUID NOT NULL
        REFERENCES app_users(user_id),

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_opportunities_status
        CHECK (opportunity_status IN ('active', 'closed')),

    CONSTRAINT ck_opportunities_outcome
        CHECK (
            close_outcome IS NULL
            OR close_outcome IN ('won', 'lost', 'cancelled', 'other')
        ),

    CONSTRAINT ck_opportunities_estimated_revenue
        CHECK (
            estimated_revenue IS NULL
            OR estimated_revenue >= 0
        ),

    CONSTRAINT ck_opportunities_actual_revenue
        CHECK (
            actual_revenue IS NULL
            OR actual_revenue >= 0
        ),

    CONSTRAINT ck_opportunities_dates
        CHECK (
            closed_date IS NULL
            OR closed_date >= active_date
        ),

    CONSTRAINT ck_opportunities_closed_state
        CHECK (
            opportunity_status = 'active'
            OR closed_date IS NOT NULL
        )
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_opportunities_external_source
    ON opportunities(source_system, external_opportunity_id)
    WHERE external_opportunity_id <> '';

CREATE INDEX IF NOT EXISTS idx_opportunities_status_updated
    ON opportunities(opportunity_status, updated_at DESC);

CREATE INDEX IF NOT EXISTS idx_opportunities_client
    ON opportunities(client_id);

CREATE INDEX IF NOT EXISTS idx_opportunities_owner
    ON opportunities(owner_user_id);

CREATE TABLE IF NOT EXISTS opportunity_tasks (
    opportunity_task_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    opportunity_id UUID NOT NULL
        REFERENCES opportunities(opportunity_id) ON DELETE CASCADE,

    task_title TEXT NOT NULL,
    task_description TEXT NOT NULL DEFAULT '',

    assigned_role TEXT NOT NULL DEFAULT '',
    assigned_to_user_id UUID NULL
        REFERENCES app_users(user_id) ON DELETE SET NULL,

    due_date DATE NULL,
    task_status TEXT NOT NULL DEFAULT 'open',

    created_by_user_id UUID NOT NULL
        REFERENCES app_users(user_id),

    updated_by_user_id UUID NOT NULL
        REFERENCES app_users(user_id),

    completed_by_user_id UUID NULL
        REFERENCES app_users(user_id),

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ NULL,

    CONSTRAINT ck_opportunity_tasks_status
        CHECK (task_status IN ('open', 'completed', 'cancelled')),

    CONSTRAINT ck_opportunity_tasks_completed
        CHECK (
            task_status <> 'completed'
            OR completed_at IS NOT NULL
        )
);

CREATE INDEX IF NOT EXISTS idx_opportunity_tasks_opportunity
    ON opportunity_tasks(opportunity_id, task_status, updated_at DESC);

CREATE INDEX IF NOT EXISTS idx_opportunity_tasks_assignee
    ON opportunity_tasks(assigned_to_user_id, task_status);

CREATE TABLE IF NOT EXISTS opportunity_events (
    opportunity_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    opportunity_id UUID NOT NULL
        REFERENCES opportunities(opportunity_id) ON DELETE CASCADE,

    opportunity_task_id UUID NULL
        REFERENCES opportunity_tasks(opportunity_task_id) ON DELETE SET NULL,

    event_type TEXT NOT NULL,
    event_details_json JSONB NOT NULL DEFAULT '{}'::jsonb,

    actor_user_id UUID NOT NULL
        REFERENCES app_users(user_id),

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_opportunity_events_timeline
    ON opportunity_events(opportunity_id, created_at DESC);

COMMENT ON TABLE opportunities IS
    'Module 063 opportunity lifecycle records for Sales, Presales, and Engineering collaboration.';

COMMENT ON TABLE opportunity_tasks IS
    'Module 063 collaborative action items with creator, updater, assignee, and completion accountability.';

COMMENT ON TABLE opportunity_events IS
    'Module 063 immutable activity timeline for opportunity and task changes.';

COMMIT;
