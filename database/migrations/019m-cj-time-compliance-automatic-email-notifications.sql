-- 019M-CJ Time Compliance Automatic Engineer Email Notifications

CREATE TABLE IF NOT EXISTS time_compliance_notification_runs (
    time_compliance_notification_run_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    run_type text NOT NULL DEFAULT 'manual',
    scenario text NOT NULL DEFAULT 'weekly_reminder',
    delivery_mode text NOT NULL DEFAULT 'outbox_only',
    week_start date NULL,
    week_end date NULL,
    requested_by_user_id uuid NULL,
    requested_by_email text NULL,
    run_status text NOT NULL DEFAULT 'created',
    generated_count integer NOT NULL DEFAULT 0,
    queued_count integer NOT NULL DEFAULT 0,
    sent_count integer NOT NULL DEFAULT 0,
    failed_count integer NOT NULL DEFAULT 0,
    skipped_count integer NOT NULL DEFAULT 0,
    started_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz NULL,
    run_message text NULL,
    preview_snapshot jsonb NOT NULL DEFAULT '{}'::jsonb
);

CREATE INDEX IF NOT EXISTS idx_time_compliance_notification_runs_started
    ON time_compliance_notification_runs(started_at DESC);

CREATE INDEX IF NOT EXISTS idx_time_compliance_notification_runs_scenario
    ON time_compliance_notification_runs(scenario);

CREATE TABLE IF NOT EXISTS time_compliance_notification_delivery_events (
    time_compliance_notification_delivery_event_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    time_compliance_notification_run_id uuid NOT NULL REFERENCES time_compliance_notification_runs(time_compliance_notification_run_id) ON DELETE CASCADE,
    recipient_user_id uuid NULL,
    recipient_email text NOT NULL,
    recipient_display_name text NULL,
    manager_email text NULL,
    cc_emails text[] NOT NULL DEFAULT ARRAY[]::text[],
    subject text NOT NULL,
    body text NOT NULL,
    delivery_status text NOT NULL DEFAULT 'queued',
    delivery_mode text NOT NULL DEFAULT 'outbox_only',
    sent_at timestamptz NULL,
    failed_at timestamptz NULL,
    failure_message text NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_time_compliance_delivery_run
    ON time_compliance_notification_delivery_events(time_compliance_notification_run_id);

CREATE INDEX IF NOT EXISTS idx_time_compliance_delivery_status
    ON time_compliance_notification_delivery_events(delivery_status);

CREATE INDEX IF NOT EXISTS idx_time_compliance_delivery_recipient
    ON time_compliance_notification_delivery_events(lower(recipient_email));

CREATE TABLE IF NOT EXISTS time_compliance_notification_schedule_controls (
    time_compliance_notification_schedule_control_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    schedule_key text NOT NULL UNIQUE,
    schedule_name text NOT NULL,
    scenario text NOT NULL,
    recipient_group_code text NOT NULL,
    send_day text NOT NULL,
    send_time_local text NOT NULL,
    timezone_name text NOT NULL DEFAULT 'America/Chicago',
    is_active boolean NOT NULL DEFAULT true,
    requires_preview_before_send boolean NOT NULL DEFAULT true,
    last_run_at timestamptz NULL,
    next_run_hint text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

INSERT INTO time_compliance_notification_schedule_controls (
    schedule_key,
    schedule_name,
    scenario,
    recipient_group_code,
    send_day,
    send_time_local,
    timezone_name,
    is_active,
    requires_preview_before_send,
    next_run_hint
)
VALUES
    (
        'WEEKLY_ENGINEER_TIME_REMINDER',
        'Weekly engineer time reminder',
        'weekly_reminder',
        'ENGINEERS',
        'Monday',
        '06:00',
        'America/Chicago',
        true,
        true,
        'Every Monday at 6:00 AM Central'
    ),
    (
        'WEEKLY_ENGINEER_TIME_ESCALATION',
        'Weekly engineer time escalation',
        'weekly_escalation',
        'ENGINEERS_MANAGERS_PTC',
        'Monday',
        '08:00',
        'America/Chicago',
        true,
        true,
        'Every Monday at 8:00 AM Central'
    )
ON CONFLICT (schedule_key) DO UPDATE
SET
    schedule_name = EXCLUDED.schedule_name,
    scenario = EXCLUDED.scenario,
    recipient_group_code = EXCLUDED.recipient_group_code,
    send_day = EXCLUDED.send_day,
    send_time_local = EXCLUDED.send_time_local,
    timezone_name = EXCLUDED.timezone_name,
    requires_preview_before_send = EXCLUDED.requires_preview_before_send,
    next_run_hint = EXCLUDED.next_run_hint,
    updated_at = now();

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
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.time_compliance_notification_runs TO %I', role_record.rolname);
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.time_compliance_notification_delivery_events TO %I', role_record.rolname);
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.time_compliance_notification_schedule_controls TO %I', role_record.rolname);
    END LOOP;
END $$;
