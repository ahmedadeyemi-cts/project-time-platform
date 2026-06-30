-- 019M-AI Cost Overrun Alert Foundation

CREATE TABLE IF NOT EXISTS project_cost_alerts (
    project_cost_alert_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id uuid NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    alert_key character varying(180) NOT NULL UNIQUE,
    alert_type character varying(80) NOT NULL,
    alert_severity character varying(40) NOT NULL DEFAULT 'medium',
    alert_status character varying(40) NOT NULL DEFAULT 'open',
    client_name text,
    project_code character varying(120),
    project_name text,
    project_manager_user_id uuid,
    project_manager_email character varying(255),
    planned_engineering_cost numeric(14,2) NOT NULL DEFAULT 0,
    planned_pm_cost numeric(14,2) NOT NULL DEFAULT 0,
    planned_total_project_cost numeric(14,2) NOT NULL DEFAULT 0,
    assigned_hours numeric(12,2) NOT NULL DEFAULT 0,
    used_hours numeric(12,2) NOT NULL DEFAULT 0,
    remaining_assigned_hours numeric(12,2) NOT NULL DEFAULT 0,
    over_assigned_hours numeric(12,2) NOT NULL DEFAULT 0,
    cost_status character varying(80) NOT NULL DEFAULT 'unknown',
    alert_summary text NOT NULL,
    alert_detail text NOT NULL,
    first_detected_at timestamp with time zone NOT NULL DEFAULT now(),
    last_detected_at timestamp with time zone NOT NULL DEFAULT now(),
    notification_queued_at timestamp with time zone,
    notification_recipient_count integer NOT NULL DEFAULT 0,
    resolved_at timestamp with time zone,
    metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_project_cost_alerts_project_id
    ON project_cost_alerts(project_id);

CREATE INDEX IF NOT EXISTS ix_project_cost_alerts_status_severity
    ON project_cost_alerts(alert_status, alert_severity);

CREATE INDEX IF NOT EXISTS ix_project_cost_alerts_last_detected
    ON project_cost_alerts(last_detected_at DESC);

INSERT INTO app_permissions (
    permission_code,
    permission_name,
    module_code,
    permission_description
)
VALUES
    ('VIEW_COST_ALERTS', 'View Cost Alerts', 'PROJECT_COST_ALERTS', 'View project cost plan, over-assignment, and cost readiness alerts.'),
    ('MANAGE_COST_ALERTS', 'Manage Cost Alerts', 'PROJECT_COST_ALERTS', 'Evaluate cost alerts and queue cost alert notifications.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
    ON p.permission_code IN ('VIEW_COST_ALERTS', 'MANAGE_COST_ALERTS')
WHERE r.role_code IN ('ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR')
ON CONFLICT DO NOTHING;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
    ON p.permission_code = 'VIEW_COST_ALERTS'
WHERE r.role_code IN ('PROJECT_MANAGEMENT', 'PROJECT_MANAGER', 'MANAGER')
ON CONFLICT DO NOTHING;

GRANT SELECT, INSERT, UPDATE ON TABLE project_cost_alerts TO "ptp_app";
GRANT SELECT, INSERT, UPDATE ON TABLE notification_outbox TO "ptp_app";
GRANT SELECT, INSERT, UPDATE ON TABLE email_notification_outbox TO "ptp_app";
