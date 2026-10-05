BEGIN;

UPDATE enterprise_notification_policies
SET recipient_strategy = 'project_team',
    recipient_configuration = '{"to":["project_manager","ptc","billing"]}'::jsonb,
    subject_template = 'ProjectPulse: Project closeout started — {{projectCode}}',
    text_template = 'Closeout started for {{projectCode}} {{projectName}}.',
    updated_at = NOW()
WHERE policy_code = 'CLOSEOUT_STARTED';

UPDATE enterprise_notification_policies
SET recipient_strategy = 'project_team',
    recipient_configuration = '{"to":["project_manager","ptc","billing","account_executive"]}'::jsonb,
    subject_template = 'ProjectPulse: Project closeout complete — {{projectCode}}',
    text_template = 'Closeout completed for {{projectCode}} {{projectName}}.',
    updated_at = NOW()
WHERE policy_code = 'CLOSEOUT_COMPLETED';

COMMIT;
