-- 022C Production Notification Preferences + Routing Rules

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS production_notification_routing_rules (
    production_notification_routing_rule_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    rule_key varchar(160) NOT NULL UNIQUE,
    module_key varchar(100) NOT NULL,
    severity varchar(32) NOT NULL DEFAULT 'info',
    target_role_codes text[] NOT NULL DEFAULT ARRAY[]::text[],
    default_in_app_enabled boolean NOT NULL DEFAULT TRUE,
    allow_user_opt_out boolean NOT NULL DEFAULT TRUE,
    allow_email_delivery boolean NOT NULL DEFAULT FALSE,
    is_active boolean NOT NULL DEFAULT TRUE,
    rule_description text,
    created_at timestamptz NOT NULL DEFAULT NOW(),
    updated_at timestamptz NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_production_notification_routing_rules_active
    ON production_notification_routing_rules(is_active, module_key, severity);

CREATE INDEX IF NOT EXISTS idx_production_notification_routing_rules_roles
    ON production_notification_routing_rules USING gin(target_role_codes);

CREATE TABLE IF NOT EXISTS production_notification_user_preferences (
    production_notification_user_preference_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    module_key varchar(100) NOT NULL,
    severity varchar(32) NOT NULL DEFAULT 'info',
    in_app_enabled boolean NOT NULL DEFAULT TRUE,
    email_enabled boolean NOT NULL DEFAULT FALSE,
    muted_until timestamptz NULL,
    updated_by_user_id uuid NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    created_at timestamptz NOT NULL DEFAULT NOW(),
    updated_at timestamptz NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_production_notification_user_preference UNIQUE (user_id, module_key, severity)
);

CREATE INDEX IF NOT EXISTS idx_production_notification_user_preferences_user
    ON production_notification_user_preferences(user_id, module_key, severity);

INSERT INTO production_notification_routing_rules (
    rule_key,
    module_key,
    severity,
    target_role_codes,
    default_in_app_enabled,
    allow_user_opt_out,
    allow_email_delivery,
    is_active,
    rule_description
)
VALUES
(
    '022C_PRODUCTION_READINESS_WARNINGS',
    'PRODUCTION_READINESS',
    'warning',
    ARRAY['ADMINISTRATOR','PROJECT_TEAM_COORDINATOR','MANAGER','EXECUTIVE'],
    TRUE,
    TRUE,
    FALSE,
    TRUE,
    'Routes production readiness warnings to operational leadership in-app only.'
),
(
    '022C_PRODUCTION_READINESS_CRITICAL',
    'PRODUCTION_READINESS',
    'critical',
    ARRAY['ADMINISTRATOR','PROJECT_TEAM_COORDINATOR','MANAGER','EXECUTIVE'],
    TRUE,
    FALSE,
    FALSE,
    TRUE,
    'Routes critical production readiness alerts to operational leadership in-app only.'
),
(
    '022C_TIME_COMPLIANCE_WARNINGS',
    'TIME_COMPLIANCE',
    'warning',
    ARRAY['ADMINISTRATOR','PROJECT_TEAM_COORDINATOR','MANAGER'],
    TRUE,
    TRUE,
    FALSE,
    TRUE,
    'Routes time compliance warnings to administrators, PTC, and managers in-app only.'
),
(
    '022C_APPROVAL_WORKFLOW_WARNINGS',
    'APPROVAL_WORKFLOW',
    'warning',
    ARRAY['ADMINISTRATOR','PROJECT_TEAM_COORDINATOR','MANAGER','PROJECT_MANAGEMENT','PROJECT_MANAGER'],
    TRUE,
    TRUE,
    FALSE,
    TRUE,
    'Routes approval workflow warnings to approval owners in-app only.'
),
(
    '022C_EXPORT_RECONCILIATION_WARNINGS',
    'EXPORT_RECONCILIATION',
    'warning',
    ARRAY['ADMINISTRATOR','PROJECT_TEAM_COORDINATOR','ACCOUNTING'],
    TRUE,
    TRUE,
    FALSE,
    TRUE,
    'Routes export and reconciliation warnings to operations and accounting in-app only.'
),
(
    '022C_SYSTEM_INFO',
    'SYSTEM',
    'info',
    ARRAY['ADMINISTRATOR','PROJECT_TEAM_COORDINATOR'],
    TRUE,
    TRUE,
    FALSE,
    TRUE,
    'Routes system informational notices to administrators and PTC in-app only.'
)
ON CONFLICT (rule_key) DO UPDATE
SET
    module_key = EXCLUDED.module_key,
    severity = EXCLUDED.severity,
    target_role_codes = EXCLUDED.target_role_codes,
    default_in_app_enabled = EXCLUDED.default_in_app_enabled,
    allow_user_opt_out = EXCLUDED.allow_user_opt_out,
    allow_email_delivery = FALSE,
    is_active = EXCLUDED.is_active,
    rule_description = EXCLUDED.rule_description,
    updated_at = NOW();

DO $$
DECLARE
    role_record record;
BEGIN
    FOR role_record IN
        SELECT rolname
        FROM pg_roles
        WHERE rolname IN ('projecttime_app', 'projectpulse_app', 'ptp_app', 'ptp_api', 'app_user')
           OR rolname ILIKE '%projecttime%'
           OR rolname ILIKE '%projectpulse%'
    LOOP
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.production_notification_routing_rules TO %I', role_record.rolname);
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.production_notification_user_preferences TO %I', role_record.rolname);
    END LOOP;
END $$;
