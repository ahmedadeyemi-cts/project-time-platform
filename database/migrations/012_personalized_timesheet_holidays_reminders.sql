-- Project Health Dashboard
-- Migration: 012_personalized_timesheet_holidays_reminders.sql
-- Purpose: Add hyper-personalized timesheet defaults, holiday upload foundation, and reminder/notification foundation.

BEGIN;

-- Ensure Vacation and Holiday are valid selectable non-project codes.
INSERT INTO non_project_time_categories (
    category_code,
    category_name,
    category_description,
    utilization_classification,
    utilization_bucket,
    requires_approval,
    is_active,
    display_order
)
VALUES
    ('VACATION', 'Vacation', 'PTO / vacation time. Use Vacation for PTO only.', 'non_billable', 'pto', FALSE, TRUE, 80),
    ('HOLIDAY', 'Holiday', 'Company-paid holiday or floating holiday time.', 'non_billable', 'holiday', FALSE, TRUE, 90)
ON CONFLICT (category_code) DO UPDATE
SET category_name = EXCLUDED.category_name,
    category_description = EXCLUDED.category_description,
    utilization_classification = EXCLUDED.utilization_classification,
    utilization_bucket = EXCLUDED.utilization_bucket,
    requires_approval = EXCLUDED.requires_approval,
    is_active = TRUE,
    display_order = EXCLUDED.display_order,
    updated_at = NOW();

-- Per-user timesheet preferences. Default rows are no longer global; they are user controlled.
CREATE TABLE IF NOT EXISTS user_timesheet_preferences (
    user_timesheet_preference_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    default_non_project_category_codes TEXT[] NOT NULL DEFAULT ARRAY[]::TEXT[],
    default_project_task_ids UUID[] NOT NULL DEFAULT ARRAY[]::UUID[],
    auto_add_holidays BOOLEAN NOT NULL DEFAULT TRUE,
    weekly_reminder_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    reminder_day_of_week INTEGER NOT NULL DEFAULT 5,
    reminder_local_time TIME NOT NULL DEFAULT TIME '09:00',
    timezone_name VARCHAR(100) NOT NULL DEFAULT 'America/Los_Angeles',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(user_id)
);

-- Annual company holiday upload batches.
CREATE TABLE IF NOT EXISTS holiday_upload_batches (
    holiday_upload_batch_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    upload_year INTEGER NOT NULL,
    original_filename VARCHAR(255),
    uploaded_by_user_id UUID REFERENCES app_users(user_id),
    uploaded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    row_count INTEGER NOT NULL DEFAULT 0,
    notes TEXT,
    UNIQUE(upload_year, original_filename)
);

-- Company holidays by date. These drive automatic 8-hour Holiday time population.
CREATE TABLE IF NOT EXISTS company_holidays (
    company_holiday_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    holiday_date DATE NOT NULL UNIQUE,
    holiday_name VARCHAR(255) NOT NULL,
    holiday_code VARCHAR(75) NOT NULL DEFAULT 'HOLIDAY',
    holiday_type VARCHAR(50) NOT NULL DEFAULT 'company_paid',
    is_floating_holiday BOOLEAN NOT NULL DEFAULT FALSE,
    auto_populate_hours NUMERIC(5,2) NOT NULL DEFAULT 8.00,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    source_batch_id UUID REFERENCES holiday_upload_batches(holiday_upload_batch_id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Notification groups make reminders controllable without hard-coding recipients.
CREATE TABLE IF NOT EXISTS notification_groups (
    notification_group_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    group_code VARCHAR(75) NOT NULL UNIQUE,
    group_name VARCHAR(255) NOT NULL,
    group_description TEXT,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS notification_group_members (
    notification_group_member_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    notification_group_id UUID NOT NULL REFERENCES notification_groups(notification_group_id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(notification_group_id, user_id)
);

CREATE TABLE IF NOT EXISTS reminder_rules (
    reminder_rule_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    rule_code VARCHAR(100) NOT NULL UNIQUE,
    rule_name VARCHAR(255) NOT NULL,
    recipient_group_code VARCHAR(75) NOT NULL REFERENCES notification_groups(group_code),
    rule_type VARCHAR(50) NOT NULL,
    cadence_description TEXT NOT NULL,
    subject_template TEXT NOT NULL,
    body_template TEXT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS email_notification_outbox (
    email_notification_outbox_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    rule_code VARCHAR(100),
    recipient_email VARCHAR(255) NOT NULL,
    recipient_name VARCHAR(255),
    subject TEXT NOT NULL,
    body TEXT NOT NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'queued',
    scheduled_for TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    sent_at TIMESTAMPTZ,
    error_message TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Seed notification groups.
INSERT INTO notification_groups (group_code, group_name, group_description)
VALUES
    ('ENGINEERS', 'Engineers', 'Resources required to enter and submit weekly time.'),
    ('PROJECT_MANAGEMENT', 'Project Management Team', 'Project managers and delivery leaders who need month-end reminders.')
ON CONFLICT (group_code) DO UPDATE
SET group_name = EXCLUDED.group_name,
    group_description = EXCLUDED.group_description,
    is_active = TRUE,
    updated_at = NOW();

-- Seed group membership from known development users and titles.
INSERT INTO notification_group_members (notification_group_id, user_id, is_active)
SELECT ng.notification_group_id, u.user_id, TRUE
FROM notification_groups ng
CROSS JOIN app_users u
WHERE ng.group_code = 'ENGINEERS'
  AND u.is_active = TRUE
  AND (
      u.email = 'ahmed.adeyemi@ussignal.com'
      OR u.job_title ILIKE '%engineer%'
      OR u.department ILIKE '%professional services%'
  )
ON CONFLICT (notification_group_id, user_id) DO UPDATE
SET is_active = TRUE;

INSERT INTO notification_group_members (notification_group_id, user_id, is_active)
SELECT ng.notification_group_id, u.user_id, TRUE
FROM notification_groups ng
CROSS JOIN app_users u
WHERE ng.group_code = 'PROJECT_MANAGEMENT'
  AND u.is_active = TRUE
  AND (
      u.email = 'matthew.lenoble@ussignal.com'
      OR u.job_title ILIKE '%project manager%'
      OR u.job_title ILIKE '%pm%'
  )
ON CONFLICT (notification_group_id, user_id) DO UPDATE
SET is_active = TRUE;

-- Seed Ahmed's preference record with NO default rows. The user must choose defaults.
INSERT INTO user_timesheet_preferences (user_id, default_non_project_category_codes, default_project_task_ids, auto_add_holidays, weekly_reminder_enabled)
SELECT u.user_id, ARRAY[]::TEXT[], ARRAY[]::UUID[], TRUE, TRUE
FROM app_users u
WHERE u.email = 'ahmed.adeyemi@ussignal.com'
ON CONFLICT (user_id) DO UPDATE
SET auto_add_holidays = TRUE,
    weekly_reminder_enabled = TRUE,
    updated_at = NOW();

-- Reminder rules.
INSERT INTO reminder_rules (
    rule_code,
    rule_name,
    recipient_group_code,
    rule_type,
    cadence_description,
    subject_template,
    body_template
)
VALUES
    (
        'WEEKLY_ENGINEER_TIME_REMINDER',
        'Weekly engineer time reminder',
        'ENGINEERS',
        'weekly',
        'Every Friday at 9:00 AM server/local reminder time by default.',
        'Reminder: Submit your weekly time in Project Health Dashboard',
        'Hello {{display_name}},\n\nThis is your weekly reminder to submit your Project Health Dashboard time. All resources are required to submit 40 hours of time each week. If you are taking PTO and a time entry deadline is approaching, your time should be submitted before you take your time off.\n\nReminder: The code "Vacation" should be used for PTO. "Holiday" should be used only for company-paid holidays and your floating holiday.\n\nThank you.'
    ),
    (
        'MONTH_END_PM_REMINDER',
        'Month-end project management reminder',
        'PROJECT_MANAGEMENT',
        'month_end_last_friday',
        'Runs on the last Friday of each month.',
        'Month End Reminder: Project Health Dashboard review',
        'Hello {{display_name}},\n\nThis is a Month End reminder for the Project Management team. Please review project time, approvals, billing readiness, expenses, and reporting items in Project Health Dashboard.\n\nThank you.'
    )
ON CONFLICT (rule_code) DO UPDATE
SET rule_name = EXCLUDED.rule_name,
    recipient_group_code = EXCLUDED.recipient_group_code,
    rule_type = EXCLUDED.rule_type,
    cadence_description = EXCLUDED.cadence_description,
    subject_template = EXCLUDED.subject_template,
    body_template = EXCLUDED.body_template,
    is_active = TRUE,
    updated_at = NOW();

INSERT INTO schema_migrations (migration_id, description)
VALUES ('012_personalized_timesheet_holidays_reminders', 'Add user-controlled defaults, holiday upload foundation, and reminder notification foundation')
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
