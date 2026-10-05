-- Finance billing handoff notifications for delivery completion and final closeout.
BEGIN;

UPDATE enterprise_notification_policies
SET recipient_strategy = 'billing_project_team',
    recipient_configuration = '{"to":["billing","finance","accounting"],"cc":["project_manager","ptc"]}'::jsonb,
    subject_template = 'ProjectPulse: Ready for billing — {{projectCode}}',
    text_template = 'Delivery is complete for {{projectCode}} {{projectName}}. Finance, Billing, and Accounting can review billing readiness and create the appropriate invoice package in Pulse.',
    updated_at = NOW()
WHERE policy_code = 'CLOSEOUT_STARTED';

UPDATE enterprise_notification_policies
SET recipient_strategy = 'billing_project_team',
    recipient_configuration = '{"to":["billing","finance","accounting"],"cc":["project_manager","ptc"]}'::jsonb,
    subject_template = 'ProjectPulse: Project closeout complete — {{projectCode}}',
    text_template = 'Final closeout is complete for {{projectCode}} {{projectName}}. The billing decision and closeout evidence remain available in Pulse.',
    updated_at = NOW()
WHERE policy_code = 'CLOSEOUT_COMPLETED';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM enterprise_notification_policies
        WHERE policy_code = 'CLOSEOUT_STARTED'
          AND recipient_strategy = 'billing_project_team'
    ) OR NOT EXISTS (
        SELECT 1 FROM enterprise_notification_policies
        WHERE policy_code = 'CLOSEOUT_COMPLETED'
          AND recipient_strategy = 'billing_project_team'
    ) THEN
        RAISE EXCEPTION 'Finance billing handoff notification policy verification failed';
    END IF;
END $$;

COMMIT;
