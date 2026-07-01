-- 022A Production Notification Center Foundation
-- Creates role-aware in-app production notifications without sending email.

CREATE TABLE IF NOT EXISTS production_notification_events (
    production_notification_event_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    notification_key text NOT NULL UNIQUE,
    module_key text NOT NULL,
    severity text NOT NULL DEFAULT 'info',
    title text NOT NULL,
    body text NOT NULL,
    target_user_id uuid NULL,
    target_role_codes text[] NOT NULL DEFAULT ARRAY[]::text[],
    source_route text NULL,
    source_entity_type text NULL,
    source_entity_id text NULL,
    action_url text NULL,
    is_active boolean NOT NULL DEFAULT true,
    expires_at timestamptz NULL,
    created_by_user_id uuid NULL,
    created_by_email text NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_production_notification_events_active
    ON production_notification_events(is_active, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_production_notification_events_target_user
    ON production_notification_events(target_user_id, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_production_notification_events_module
    ON production_notification_events(module_key, created_at DESC);

CREATE TABLE IF NOT EXISTS production_notification_acknowledgments (
    production_notification_acknowledgment_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    notification_id uuid NOT NULL REFERENCES production_notification_events(production_notification_event_id) ON DELETE CASCADE,
    acknowledged_by_user_id uuid NOT NULL,
    acknowledged_by_email text NULL,
    acknowledged_at timestamptz NOT NULL DEFAULT now(),
    acknowledgment_note text NULL,
    UNIQUE(notification_id, acknowledged_by_user_id)
);

CREATE INDEX IF NOT EXISTS idx_production_notification_acknowledgments_user
    ON production_notification_acknowledgments(acknowledged_by_user_id, acknowledged_at DESC);

INSERT INTO production_notification_events (
    notification_key,
    module_key,
    severity,
    title,
    body,
    target_role_codes,
    source_route,
    source_entity_type,
    action_url,
    created_by_email
)
VALUES
(
    '022A_PRODUCTION_NOTIFICATION_CENTER_READY',
    '022A',
    'info',
    'Production notification center is ready',
    'Production notification center foundation is available for role-aware in-app alerts. No email is sent by this module.',
    ARRAY['ADMINISTRATOR','PROJECT_TEAM_COORDINATOR'],
    'dashboard',
    'production_notification_center',
    '#dashboard',
    'system'
)
ON CONFLICT (notification_key) DO UPDATE
SET
    module_key = EXCLUDED.module_key,
    severity = EXCLUDED.severity,
    title = EXCLUDED.title,
    body = EXCLUDED.body,
    target_role_codes = EXCLUDED.target_role_codes,
    source_route = EXCLUDED.source_route,
    source_entity_type = EXCLUDED.source_entity_type,
    action_url = EXCLUDED.action_url,
    is_active = true;

DO $$
DECLARE
    role_record record;
BEGIN
    FOR role_record IN
        SELECT rolname
        FROM pg_roles
        WHERE rolcanlogin = true
          AND rolname <> 'postgres'
    LOOP
        EXECUTE format('GRANT USAGE ON SCHEMA public TO %I', role_record.rolname);
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.production_notification_events TO %I', role_record.rolname);
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.production_notification_acknowledgments TO %I', role_record.rolname);
    END LOOP;
END $$;
