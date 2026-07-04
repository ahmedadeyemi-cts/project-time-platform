-- 019M-O Time Compliance & Notification Center defaults
-- Safe/idempotent. Dry-run only; does not send email.

INSERT INTO reminder_rules (
    rule_code,
    rule_name,
    recipient_group_code,
    rule_type,
    cadence_description,
    subject_template,
    body_template,
    is_active
)
VALUES
(
    'WEEKLY_ENGINEER_TIME_REMINDER',
    'Weekly engineer time reminder',
    'ENGINEERS',
    'weekly',
    'Every Monday at 6:00 AM Central. Dry-run preview required before real send.',
    'Reminder: Submit your weekly time in Project Health Dashboard',
    'Project Health Dashboard shows that your weekly time has not been submitted. Please review and submit your time. Manager and Project Team Coordinator are copied when configured.',
    TRUE
),
(
    'WEEKLY_ENGINEER_TIME_ESCALATION',
    'Weekly engineer time escalation',
    'ENGINEERS_MANAGERS_PTC',
    'weekly_escalation',
    'Every Monday at 8:00 AM Central. Dry-run preview required before real send.',
    'Escalation: Missing weekly time in Project Health Dashboard',
    'Project Health Dashboard shows missing weekly time after the reminder window. Engineer, manager, and Project Team Coordinator should be included when configured.',
    TRUE
),
(
    'MONTH_END_PM_REMINDER',
    'Month-end project management reminder',
    'PROJECT_MANAGEMENT',
    'month_end_last_business_day',
    'Runs on the selected last weekday of the month: Monday, Tuesday, Wednesday, Thursday, or Friday. Default: last Friday.',
    'Month End Reminder: Project Health Dashboard review',
    'Please review project time, approvals, billing readiness, expenses, and reporting items in Project Health Dashboard before month-end close.',
    TRUE
),
(
    'HOLIDAY_TIME_REMINDER_7_DAY',
    'Holiday time reminder - 7 day',
    'ENGINEERS',
    'holiday_7_day',
    'Runs 7 days before active weekday company holidays.',
    'Upcoming company holiday: time entry reminder',
    'A company holiday is approaching. Please confirm time entry expectations in Project Health Dashboard.',
    TRUE
),
(
    'HOLIDAY_TIME_REMINDER_1_DAY',
    'Holiday time reminder - 1 day',
    'ENGINEERS',
    'holiday_1_day',
    'Runs 1 day before active weekday company holidays.',
    'Company holiday tomorrow: time entry reminder',
    'A company holiday is tomorrow. Please confirm time entry expectations in Project Health Dashboard.',
    TRUE
)
ON CONFLICT (rule_code) DO UPDATE
SET
    rule_name = EXCLUDED.rule_name,
    recipient_group_code = EXCLUDED.recipient_group_code,
    rule_type = EXCLUDED.rule_type,
    cadence_description = EXCLUDED.cadence_description,
    subject_template = EXCLUDED.subject_template,
    body_template = EXCLUDED.body_template,
    is_active = EXCLUDED.is_active,
    updated_at = NOW();
