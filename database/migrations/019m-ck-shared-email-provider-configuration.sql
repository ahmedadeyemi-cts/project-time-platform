-- 019M-CK Shared Email Provider Configuration
-- Non-secret consumer registry only.
-- API keys and provider secrets live outside the repository in /etc/projectpulse/email.env.

CREATE TABLE IF NOT EXISTS system_email_provider_consumers (
    system_email_provider_consumer_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    consumer_key text NOT NULL UNIQUE,
    consumer_name text NOT NULL,
    consumer_description text NOT NULL,
    owning_route text NULL,
    required_permissions text[] NOT NULL DEFAULT ARRAY[]::text[],
    expected_delivery_modes text[] NOT NULL DEFAULT ARRAY['outbox_only', 'brevo_api']::text[],
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

INSERT INTO system_email_provider_consumers (
    consumer_key,
    consumer_name,
    consumer_description,
    owning_route,
    required_permissions,
    expected_delivery_modes,
    is_active
)
VALUES
    (
        'TIME_COMPLIANCE_ENGINEER_NOTIFICATIONS',
        'Time Compliance Engineer Notifications',
        'Sends engineer missing-time reminders, manager copy notices, escalation notices, and delivery evidence from the shared Project Health Dashboard email provider.',
        'time-compliance',
        ARRAY['VIEW_TIME_COMPLIANCE', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']::text[],
        ARRAY['outbox_only', 'brevo_api']::text[],
        true
    ),
    (
        'LOCAL_ADMIN_PASSWORD_RESET_NOTICES',
        'Local Admin Password Reset Notices',
        'Future consumer for temporary-password and reset-completion notification messages using the shared Project Health Dashboard email provider.',
        'manager-approval',
        ARRAY['SYSTEM_ADMINISTRATION', 'MANAGE_ALL']::text[],
        ARRAY['outbox_only', 'brevo_api']::text[],
        true
    ),
    (
        'APPROVAL_WORKFLOW_NOTIFICATIONS',
        'Approval Workflow Notifications',
        'Future consumer for submitted, returned, rejected, approved, and pending time approval notifications.',
        'workflow',
        ARRAY['VIEW_APPROVAL_WORKFLOW', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']::text[],
        ARRAY['outbox_only', 'brevo_api']::text[],
        true
    ),
    (
        'EXPORT_PACKAGE_NOTIFICATIONS',
        'Export Package Notifications',
        'Future consumer for export package readiness, download, and accounting handoff notifications.',
        'workflow',
        ARRAY['VIEW_EXPORT_PACKAGE_READINESS', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']::text[],
        ARRAY['outbox_only', 'brevo_api']::text[],
        true
    ),
    (
        'AUDIT_SIGNOFF_NOTIFICATIONS',
        'Audit Sign-Off Notifications',
        'Future consumer for production sign-off, audit evidence, and route-governance notification messages.',
        'dashboard',
        ARRAY['VIEW_PRODUCTION_READINESS_COMMAND_CENTER', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL']::text[],
        ARRAY['outbox_only', 'brevo_api']::text[],
        true
    )
ON CONFLICT (consumer_key) DO UPDATE
SET
    consumer_name = EXCLUDED.consumer_name,
    consumer_description = EXCLUDED.consumer_description,
    owning_route = EXCLUDED.owning_route,
    required_permissions = EXCLUDED.required_permissions,
    expected_delivery_modes = EXCLUDED.expected_delivery_modes,
    is_active = EXCLUDED.is_active,
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
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.system_email_provider_consumers TO %I', role_record.rolname);
    END LOOP;
END $$;
