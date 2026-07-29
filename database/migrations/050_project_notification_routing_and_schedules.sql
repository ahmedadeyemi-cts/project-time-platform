-- ProjectPulse Group 4
-- Modules 022, 023, 032, 041, and Module 065 governed mail delivery.
-- Configurable project-cost routing rules, notification schedules, recipient derivation,
-- durable dispatch evidence, delivery attempts, and role permissions.

BEGIN;

CREATE TABLE IF NOT EXISTS project_cost_alert_routing_rules (
    project_cost_alert_routing_rule_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    rule_code VARCHAR(120) NOT NULL UNIQUE,
    rule_name VARCHAR(220) NOT NULL,
    metric_code VARCHAR(100) NOT NULL CHECK (metric_code IN (
        'hours_used_percent',
        'labor_budget_used_percent',
        'expenses_used_percent',
        'forecasted_total_cost',
        'approaching_budget',
        'over_budget',
        'missing_financial_information',
        'failed_project_data_refresh'
    )),
    comparison_operator VARCHAR(24) NOT NULL DEFAULT 'gte' CHECK (comparison_operator IN (
        'gt', 'gte', 'lt', 'lte', 'eq', 'state', 'event'
    )),
    threshold_value NUMERIC(14,4) NULL,
    threshold_unit VARCHAR(40) NOT NULL DEFAULT 'percent' CHECK (threshold_unit IN (
        'percent', 'currency', 'state', 'event'
    )),
    alert_severity VARCHAR(24) NOT NULL DEFAULT 'warning' CHECK (alert_severity IN (
        'informational', 'warning', 'high', 'critical'
    )),
    recipient_roles TEXT[] NOT NULL DEFAULT ARRAY[
        'project_manager',
        'assigned_engineers',
        'solution_architect',
        'account_executive',
        'project_team_coordinator'
    ]::TEXT[],
    optional_escalation_manager_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    escalation_after_minutes INTEGER NULL CHECK (
        escalation_after_minutes IS NULL OR escalation_after_minutes BETWEEN 0 AND 43200
    ),
    delivery_boundary VARCHAR(40) NOT NULL DEFAULT 'test_only' CHECK (delivery_boundary IN (
        'test_only', 'production_governed', 'locked'
    )),
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    description TEXT NOT NULL DEFAULT '',
    created_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    updated_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_project_cost_alert_routing_rules_metric
    ON project_cost_alert_routing_rules(metric_code, enabled);

CREATE TABLE IF NOT EXISTS project_notification_schedules (
    project_notification_schedule_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    schedule_code VARCHAR(120) NOT NULL UNIQUE,
    schedule_name VARCHAR(220) NOT NULL,
    schedule_type VARCHAR(80) NOT NULL CHECK (schedule_type IN (
        'cost_alert_evaluation',
        'weekly_reminder',
        'monday_reminder',
        'month_end_reminder',
        'escalation'
    )),
    day_of_week SMALLINT NULL CHECK (day_of_week IS NULL OR day_of_week BETWEEN 0 AND 6),
    local_time TIME NOT NULL DEFAULT TIME '06:00',
    timezone_name VARCHAR(100) NOT NULL DEFAULT 'America/Chicago',
    days_before_month_end INTEGER NULL CHECK (
        days_before_month_end IS NULL OR days_before_month_end BETWEEN 0 AND 31
    ),
    escalation_after_minutes INTEGER NULL CHECK (
        escalation_after_minutes IS NULL OR escalation_after_minutes BETWEEN 0 AND 43200
    ),
    quiet_hours_start TIME NULL,
    quiet_hours_end TIME NULL,
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    delivery_boundary VARCHAR(40) NOT NULL DEFAULT 'test_only' CHECK (delivery_boundary IN (
        'test_only', 'production_governed', 'locked'
    )),
    last_started_at TIMESTAMPTZ NULL,
    last_completed_at TIMESTAMPTZ NULL,
    last_status VARCHAR(60) NOT NULL DEFAULT 'not_run',
    next_run_at TIMESTAMPTZ NULL,
    created_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    updated_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_project_notification_schedules_due
    ON project_notification_schedules(enabled, next_run_at);

CREATE TABLE IF NOT EXISTS project_notification_dispatches (
    project_notification_dispatch_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NULL REFERENCES projects(project_id) ON DELETE SET NULL,
    routing_rule_id UUID NULL REFERENCES project_cost_alert_routing_rules(project_cost_alert_routing_rule_id) ON DELETE SET NULL,
    schedule_id UUID NULL REFERENCES project_notification_schedules(project_notification_schedule_id) ON DELETE SET NULL,
    event_key VARCHAR(260) NOT NULL UNIQUE,
    notification_type VARCHAR(120) NOT NULL,
    alert_severity VARCHAR(24) NOT NULL DEFAULT 'warning',
    source_module VARCHAR(20) NOT NULL,
    source_status VARCHAR(80) NOT NULL DEFAULT 'evaluated',
    subject TEXT NOT NULL,
    text_body TEXT NOT NULL,
    html_body TEXT NOT NULL DEFAULT '',
    delivery_boundary VARCHAR(40) NOT NULL DEFAULT 'test_only',
    provider_source VARCHAR(40) NOT NULL DEFAULT 'module_065',
    delivery_status VARCHAR(40) NOT NULL DEFAULT 'preview_ready' CHECK (delivery_status IN (
        'preview_ready', 'held', 'queued', 'sending', 'sent', 'failed', 'suppressed'
    )),
    scheduled_for TIMESTAMPTZ NULL,
    released_at TIMESTAMPTZ NULL,
    released_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    sent_at TIMESTAMPTZ NULL,
    provider_message_id TEXT NULL,
    last_error_code VARCHAR(120) NOT NULL DEFAULT '',
    last_error_message TEXT NOT NULL DEFAULT '',
    metadata_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_project_notification_dispatches_status
    ON project_notification_dispatches(delivery_status, scheduled_for, created_at);
CREATE INDEX IF NOT EXISTS ix_project_notification_dispatches_project
    ON project_notification_dispatches(project_id, created_at DESC);

CREATE TABLE IF NOT EXISTS project_notification_dispatch_recipients (
    project_notification_dispatch_recipient_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_notification_dispatch_id UUID NOT NULL REFERENCES project_notification_dispatches(project_notification_dispatch_id) ON DELETE CASCADE,
    recipient_role VARCHAR(100) NOT NULL,
    recipient_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    recipient_name TEXT NOT NULL DEFAULT '',
    recipient_email VARCHAR(320) NOT NULL,
    recipient_type VARCHAR(12) NOT NULL DEFAULT 'to' CHECK (recipient_type IN ('to', 'cc', 'bcc')),
    derivation_source VARCHAR(120) NOT NULL,
    delivery_status VARCHAR(40) NOT NULL DEFAULT 'pending' CHECK (delivery_status IN (
        'pending', 'sent', 'failed', 'suppressed'
    )),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_project_notification_dispatch_recipients_dispatch
    ON project_notification_dispatch_recipients(project_notification_dispatch_id, recipient_type);
CREATE UNIQUE INDEX IF NOT EXISTS ux_project_notification_dispatch_recipients_email
    ON project_notification_dispatch_recipients(
        project_notification_dispatch_id,
        lower(recipient_email),
        recipient_type
    );

CREATE TABLE IF NOT EXISTS project_notification_delivery_attempts (
    project_notification_delivery_attempt_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_notification_dispatch_id UUID NOT NULL REFERENCES project_notification_dispatches(project_notification_dispatch_id) ON DELETE RESTRICT,
    attempt_number INTEGER NOT NULL CHECK (attempt_number > 0),
    provider_source VARCHAR(40) NOT NULL DEFAULT 'module_065',
    configured_provider VARCHAR(80) NOT NULL DEFAULT 'locked',
    recipient_boundary VARCHAR(40) NOT NULL DEFAULT 'locked',
    attempt_status VARCHAR(40) NOT NULL CHECK (attempt_status IN (
        'suppressed', 'queued', 'sent', 'failed'
    )),
    provider_message_id TEXT NULL,
    diagnostic_code VARCHAR(120) NOT NULL DEFAULT '',
    diagnostic_message TEXT NOT NULL DEFAULT '',
    attempted_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(project_notification_dispatch_id, attempt_number)
);

CREATE INDEX IF NOT EXISTS ix_project_notification_delivery_attempts_dispatch
    ON project_notification_delivery_attempts(project_notification_dispatch_id, attempted_at DESC);

CREATE TABLE IF NOT EXISTS project_notification_configuration_audit (
    project_notification_configuration_audit_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    entity_type VARCHAR(80) NOT NULL CHECK (entity_type IN ('routing_rule', 'schedule', 'dispatch')),
    entity_id UUID NOT NULL,
    action_code VARCHAR(100) NOT NULL,
    actor_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    change_reason TEXT NOT NULL DEFAULT '',
    prior_json JSONB NULL,
    new_json JSONB NULL,
    correlation_id VARCHAR(160) NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_project_notification_configuration_audit_entity
    ON project_notification_configuration_audit(entity_type, entity_id, created_at DESC);

CREATE OR REPLACE FUNCTION projectpulse050_block_notification_evidence_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse050_immutable$
BEGIN
    RAISE EXCEPTION 'Project notification delivery and configuration audit evidence is immutable.';
END;
$projectpulse050_immutable$;

DROP TRIGGER IF EXISTS trg_projectpulse050_delivery_attempts_immutable
    ON project_notification_delivery_attempts;
CREATE TRIGGER trg_projectpulse050_delivery_attempts_immutable
BEFORE UPDATE OR DELETE ON project_notification_delivery_attempts
FOR EACH ROW EXECUTE FUNCTION projectpulse050_block_notification_evidence_mutation();

DROP TRIGGER IF EXISTS trg_projectpulse050_configuration_audit_immutable
    ON project_notification_configuration_audit;
CREATE TRIGGER trg_projectpulse050_configuration_audit_immutable
BEFORE UPDATE OR DELETE ON project_notification_configuration_audit
FOR EACH ROW EXECUTE FUNCTION projectpulse050_block_notification_evidence_mutation();

INSERT INTO project_cost_alert_routing_rules (
    rule_code,
    rule_name,
    metric_code,
    comparison_operator,
    threshold_value,
    threshold_unit,
    alert_severity,
    recipient_roles,
    escalation_after_minutes,
    delivery_boundary,
    description
)
VALUES
    ('HOURS_USED_APPROACHING', 'Hours used approaching allocation', 'hours_used_percent', 'gte', 80, 'percent', 'warning', ARRAY['project_manager','assigned_engineers','project_team_coordinator'], 1440, 'test_only', 'Notify the project team when used hours reach eighty percent of planned hours.'),
    ('LABOR_BUDGET_APPROACHING', 'Labor budget approaching limit', 'labor_budget_used_percent', 'gte', 80, 'percent', 'high', ARRAY['project_manager','solution_architect','account_executive','project_team_coordinator'], 720, 'test_only', 'Notify accountable project owners when calculated labor cost reaches eighty percent of the known labor budget.'),
    ('EXPENSE_BUDGET_APPROACHING', 'Expense budget approaching limit', 'expenses_used_percent', 'gte', 80, 'percent', 'warning', ARRAY['project_manager','assigned_engineers','project_team_coordinator'], 1440, 'test_only', 'Notify project owners when current Module 005 expenses reach eighty percent of the known expense budget.'),
    ('FORECAST_OVER_BUDGET', 'Forecasted final cost exceeds known project budget', 'forecasted_total_cost', 'gte', 100, 'percent', 'critical', ARRAY['project_manager','solution_architect','account_executive','project_team_coordinator'], 240, 'test_only', 'Compare forecasted final cost with the known labor and expense budget.'),
    ('PROJECT_APPROACHING_BUDGET', 'Project is approaching budget', 'approaching_budget', 'state', NULL, 'state', 'high', ARRAY['project_manager','solution_architect','account_executive','project_team_coordinator'], 720, 'test_only', 'Route the authoritative Group 3 approaching-budget state.'),
    ('PROJECT_OVER_BUDGET', 'Project is over budget', 'over_budget', 'state', NULL, 'state', 'critical', ARRAY['project_manager','assigned_engineers','solution_architect','account_executive','project_team_coordinator'], 120, 'test_only', 'Route the authoritative Group 3 over-budget state.'),
    ('MISSING_FINANCIAL_INFORMATION', 'Required project financial information is missing', 'missing_financial_information', 'event', NULL, 'event', 'warning', ARRAY['project_manager','solution_architect','account_executive','project_team_coordinator'], 1440, 'test_only', 'Notify accountable owners when budgets, rates, assignments, or commercial information are incomplete.'),
    ('PROJECT_DATA_REFRESH_FAILED', 'Project financial data refresh failed', 'failed_project_data_refresh', 'event', NULL, 'event', 'critical', ARRAY['project_team_coordinator'], 60, 'test_only', 'Escalate source-specific project financial refresh failures without blanking otherwise usable workspaces.')
ON CONFLICT (rule_code) DO UPDATE
SET rule_name = EXCLUDED.rule_name,
    metric_code = EXCLUDED.metric_code,
    comparison_operator = EXCLUDED.comparison_operator,
    threshold_value = EXCLUDED.threshold_value,
    threshold_unit = EXCLUDED.threshold_unit,
    alert_severity = EXCLUDED.alert_severity,
    recipient_roles = EXCLUDED.recipient_roles,
    escalation_after_minutes = EXCLUDED.escalation_after_minutes,
    description = EXCLUDED.description,
    updated_at = NOW();

INSERT INTO project_notification_schedules (
    schedule_code,
    schedule_name,
    schedule_type,
    day_of_week,
    local_time,
    timezone_name,
    days_before_month_end,
    escalation_after_minutes,
    quiet_hours_start,
    quiet_hours_end,
    enabled,
    delivery_boundary
)
VALUES
    ('COST_ALERT_WEEKDAY_EVALUATION', 'Weekday project-cost evaluation', 'cost_alert_evaluation', 1, TIME '07:00', 'America/Chicago', NULL, 60, TIME '20:00', TIME '06:00', TRUE, 'test_only'),
    ('WEEKLY_PROJECT_REMINDER', 'Weekly project financial reminder', 'weekly_reminder', 5, TIME '14:00', 'America/Chicago', NULL, 1440, TIME '20:00', TIME '06:00', TRUE, 'test_only'),
    ('MONDAY_PROJECT_ESCALATION', 'Monday project-cost escalation', 'monday_reminder', 1, TIME '08:00', 'America/Chicago', NULL, 120, TIME '20:00', TIME '06:00', TRUE, 'test_only'),
    ('MONTH_END_FINANCIAL_REMINDER', 'Month-end financial readiness reminder', 'month_end_reminder', NULL, TIME '09:00', 'America/Chicago', 3, 240, TIME '20:00', TIME '06:00', TRUE, 'test_only')
ON CONFLICT (schedule_code) DO UPDATE
SET schedule_name = EXCLUDED.schedule_name,
    schedule_type = EXCLUDED.schedule_type,
    day_of_week = EXCLUDED.day_of_week,
    local_time = EXCLUDED.local_time,
    timezone_name = EXCLUDED.timezone_name,
    days_before_month_end = EXCLUDED.days_before_month_end,
    escalation_after_minutes = EXCLUDED.escalation_after_minutes,
    quiet_hours_start = EXCLUDED.quiet_hours_start,
    quiet_hours_end = EXCLUDED.quiet_hours_end,
    updated_at = NOW();

INSERT INTO app_permissions (
    permission_code,
    permission_name,
    module_code,
    permission_description
)
VALUES
    ('VIEW_COST_ALERT_ROUTING_RULES', 'View Cost Alert Routing Rules', '022', 'View governed project-cost thresholds, recipients, escalation timing, and evaluation history.'),
    ('MANAGE_COST_ALERT_ROUTING_RULES', 'Manage Cost Alert Routing Rules', '022', 'Create and update governed project-cost routing thresholds and recipient roles.'),
    ('VIEW_NOTIFICATION_SCHEDULES', 'View Notification Schedules', '023', 'View weekly, Monday, month-end, escalation, quiet-hours, timezone, and delivery-boundary configuration.'),
    ('MANAGE_NOTIFICATION_SCHEDULES', 'Manage Notification Schedules', '023', 'Create and update governed project notification schedules.'),
    ('VIEW_NOTIFICATION_DELIVERY_MONITOR', 'View Notification Delivery Monitor', '032', 'View dispatches, automatically derived recipients, Module 065 readiness, and source-specific delivery results.'),
    ('MANAGE_NOTIFICATION_DELIVERY', 'Manage Notification Delivery', '032', 'Release governed notification dispatches, retry failures, and suppress invalid recipients.'),
    ('VIEW_CLOSEOUT_NOTIFICATION_ROUTING', 'View Closeout Notification Routing', '041', 'View Module 041 closeout-message routing, recipient derivation, and delivery evidence.'),
    ('DELIVER_PROJECT_NOTIFICATIONS', 'Deliver Project Notifications through Module 065', '065', 'Authorize governed Module 065 delivery after the configured recipient boundary and transport are ready.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_feature_catalog (
    feature_code,
    feature_name,
    module_code,
    route_anchor,
    required_permission_code,
    feature_description,
    display_order,
    is_active
)
VALUES
    ('COST_ALERT_ROUTING_RULES', 'Cost Alert Routing Rules', '022', '#cost-alerts', 'VIEW_COST_ALERT_ROUTING_RULES', 'Configurable project financial thresholds and automatically derived project-team recipients.', 220, TRUE),
    ('PROJECT_NOTIFICATION_SCHEDULING', 'Project Notification Scheduling', '023', '#time-compliance', 'VIEW_NOTIFICATION_SCHEDULES', 'Nontechnical scheduling for weekly, Monday, month-end, escalation, quiet-hours, timezone, and delivery boundaries.', 230, TRUE),
    ('NOTIFICATION_DELIVERY_MONITOR', 'Notification Delivery Monitor', '032', '#notification-delivery-monitor', 'VIEW_NOTIFICATION_DELIVERY_MONITOR', 'Operational monitor for project notification dispatches, recipient derivation, failures, retries, and Module 065 transport readiness.', 320, TRUE),
    ('CLOSEOUT_NOTIFICATION_ROUTING', 'Closeout Notification Routing', '041', '#closeout-email', 'VIEW_CLOSEOUT_NOTIFICATION_ROUTING', 'Module 041 closeout notification routing through the Group 4 contract and Module 065 delivery.', 410, TRUE)
ON CONFLICT (feature_code) DO UPDATE
SET feature_name = EXCLUDED.feature_name,
    module_code = EXCLUDED.module_code,
    route_anchor = EXCLUDED.route_anchor,
    required_permission_code = EXCLUDED.required_permission_code,
    feature_description = EXCLUDED.feature_description,
    display_order = EXCLUDED.display_order,
    is_active = TRUE,
    updated_at = NOW();

-- Project Management, finance, and executive users can review relevant routing evidence.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code = ANY(ARRAY[
    'VIEW_COST_ALERT_ROUTING_RULES',
    'VIEW_NOTIFICATION_SCHEDULES',
    'VIEW_NOTIFICATION_DELIVERY_MONITOR',
    'VIEW_CLOSEOUT_NOTIFICATION_ROUTING'
])
WHERE upper(role.role_code) IN (
    'PROJECT_MANAGER', 'PROJECT_MANAGEMENT',
    'PROJECT_MANAGEMENT_LEAD', 'PROJECT_MANAGEMENT_TEAM_LEAD', 'PM_TEAM_LEAD',
    'ACCOUNTING', 'ACCOUNTING_BILLING', 'BILLING', 'FINANCE', 'EXECUTIVE'
)
ON CONFLICT DO NOTHING;

-- Engineering, sales, Solution Architects, and managers receive scoped visibility.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code = ANY(
    CASE
        WHEN upper(role.role_code) = 'SOLUTION_ARCHITECT'
            THEN ARRAY['VIEW_COST_ALERT_ROUTING_RULES','VIEW_NOTIFICATION_DELIVERY_MONITOR']
        WHEN upper(role.role_code) IN ('MANAGER','ENGINEERING_LEAD','ENGINEERING_TEAM_LEAD')
            THEN ARRAY['VIEW_NOTIFICATION_SCHEDULES','VIEW_NOTIFICATION_DELIVERY_MONITOR']
        ELSE ARRAY['VIEW_NOTIFICATION_DELIVERY_MONITOR']
    END
)
WHERE upper(role.role_code) IN (
    'ENGINEERING', 'ENGINEER', 'ENGINEERING_LEAD', 'ENGINEERING_TEAM_LEAD',
    'SALES', 'INSIDE_SALES', 'SOLUTION_ARCHITECT', 'MANAGER'
)
ON CONFLICT DO NOTHING;

-- Project Team Coordinators and administrators manage routing, schedules, and delivery.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code = ANY(ARRAY[
    'VIEW_COST_ALERT_ROUTING_RULES',
    'MANAGE_COST_ALERT_ROUTING_RULES',
    'VIEW_NOTIFICATION_SCHEDULES',
    'MANAGE_NOTIFICATION_SCHEDULES',
    'VIEW_NOTIFICATION_DELIVERY_MONITOR',
    'MANAGE_NOTIFICATION_DELIVERY',
    'VIEW_CLOSEOUT_NOTIFICATION_ROUTING',
    'DELIVER_PROJECT_NOTIFICATIONS'
])
WHERE upper(role.role_code) IN (
    'PROJECT_TEAM_COORDINATOR', 'SUPER_ADMINISTRATOR', 'ADMINISTRATOR'
)
ON CONFLICT DO NOTHING;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '050_project_notification_routing_and_schedules',
    'Group 4 configurable cost routing, notification schedules, Module 032 delivery monitor, Module 041 routing, and Module 065 delivery boundary',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
