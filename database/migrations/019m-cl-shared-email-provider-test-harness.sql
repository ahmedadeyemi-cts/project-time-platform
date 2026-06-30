-- 019M-CL Shared Email Provider Test Harness
-- Stores non-secret audit evidence for single-recipient provider validation sends.

CREATE TABLE IF NOT EXISTS system_email_provider_test_events (
    system_email_provider_test_event_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    provider text NOT NULL,
    delivery_mode text NOT NULL,
    recipient_email text NOT NULL,
    recipient_display_name text NULL,
    subject text NOT NULL,
    delivery_status text NOT NULL,
    failure_message text NULL,
    requested_by_user_id uuid NULL,
    requested_by_email text NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_system_email_provider_test_events_created
    ON system_email_provider_test_events(created_at DESC);

CREATE INDEX IF NOT EXISTS idx_system_email_provider_test_events_status
    ON system_email_provider_test_events(delivery_status);

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
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.system_email_provider_test_events TO %I', role_record.rolname);
    END LOOP;
END $$;
