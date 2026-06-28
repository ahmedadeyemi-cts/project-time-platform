-- 019M-AJ Cost Alert Acknowledgement + Routing Controls

ALTER TABLE project_cost_alerts
    ADD COLUMN IF NOT EXISTS acknowledged_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS acknowledged_by_user_id uuid REFERENCES app_users(user_id),
    ADD COLUMN IF NOT EXISTS acknowledged_by_email character varying(255),
    ADD COLUMN IF NOT EXISTS acknowledgement_note text,
    ADD COLUMN IF NOT EXISTS routing_status character varying(40) NOT NULL DEFAULT 'hold',
    ADD COLUMN IF NOT EXISTS routing_note text,
    ADD COLUMN IF NOT EXISTS notification_released_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS notification_released_by_user_id uuid REFERENCES app_users(user_id),
    ADD COLUMN IF NOT EXISTS notification_released_by_email character varying(255),
    ADD COLUMN IF NOT EXISTS last_action_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS last_action_by_email character varying(255);

UPDATE project_cost_alerts
SET routing_status = CASE
        WHEN notification_queued_at IS NOT NULL THEN 'queued'
        WHEN alert_status = 'resolved' THEN 'closed'
        ELSE 'hold'
    END,
    updated_at = NOW()
WHERE routing_status IS NULL
   OR routing_status = 'hold';

CREATE INDEX IF NOT EXISTS ix_project_cost_alerts_routing_status
    ON project_cost_alerts(routing_status, notification_queued_at);

CREATE INDEX IF NOT EXISTS ix_project_cost_alerts_acknowledged_at
    ON project_cost_alerts(acknowledged_at DESC);
