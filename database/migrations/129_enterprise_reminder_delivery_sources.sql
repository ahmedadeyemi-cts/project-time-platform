BEGIN;
INSERT INTO enterprise_notification_policies(policy_code,policy_name,category,source_module,event_code,trigger_mode,
 recipient_strategy,trigger_configuration,recipient_configuration,severity,delivery_boundary,subject_template,text_template,
 producer_contract,source_state,enabled)
VALUES
 ('TIME_NOT_SUBMITTED','Time entry not submitted','time','023','time.not_submitted','scheduled','subject_user',
  '{"timezone":"America/Chicago","dayOfWeek":1,"localTime":"06:00","audienceGroup":"ENGINEERS","legacyRuleCode":"WEEKLY_ENGINEER_TIME_REMINDER"}',
  '{"to":["engineer"]}','informational','test_only','Reminder: submit time for {{weekStart}}',
  '{{engineerName}}, your time for {{weekStart}} through {{weekEnd}} has not been submitted. Please review and submit your time in Pulse. {{projectPulseUrl}}','enterprise-reminder-v1','scanner',FALSE),
 ('TIME_NOT_SUBMITTED_ESCALATION','Time entry non-submission escalation','time','023','time.not_submitted.escalation','escalation','time_non_submission_escalation',
  '{"timezone":"America/Chicago","dayOfWeek":1,"localTime":"08:00","audienceGroup":"ENGINEERS","legacyRuleCode":"WEEKLY_ENGINEER_TIME_ESCALATION"}',
  '{"to":["direct_manager","ptc"],"cc":["engineer"]}','high','test_only','Action required: time submission violation for {{engineerName}}',
  '{{engineerName}} has not submitted time for {{weekStart}} through {{weekEnd}} after the reminder window. The manager and Project Team Coordinator should follow up. {{projectPulseUrl}}','enterprise-reminder-v1','scanner',FALSE),
 ('COMPANY_HOLIDAY_UPCOMING','Upcoming company holiday','time','023','company_holiday.upcoming','scheduled','subject_user',
  '{"timezone":"America/Chicago","localTime":"06:00","offsetDays":[7,1]}','{"to":["all_active_application_users_individually"]}',
  'informational','test_only','Upcoming company holiday: {{holidayName}}',
  '{{holidayName}} is on {{holidayDate}} ({{offsetDays}} day(s) away). Review coverage and applicable time-entry expectations in Pulse. {{projectPulseUrl}}','enterprise-reminder-v1','scanner',FALSE),
 ('PM_MONTH_END_REMINDER','Month-end project management reminder','time','023','project_management.month_end','scheduled','subject_user',
  '{"timezone":"America/Chicago","dayOfWeek":5,"localTime":"08:00","audienceGroup":"PROJECT_MANAGEMENT","legacyRuleCode":"MONTH_END_PM_REMINDER"}',
  '{"to":["project_management_group_individually"]}','informational','test_only','Month-end review in Pulse',
  'Please review project time, approvals, billing readiness, expenses and reporting items before month-end close. {{projectPulseUrl}}','enterprise-reminder-v1','scanner',FALSE)
ON CONFLICT(policy_code) DO NOTHING;
INSERT INTO schema_migrations(migration_id,description) VALUES('129_enterprise_reminder_delivery_sources',
 'Opt-in governed non-submission/escalation, holiday and month-end sources; preserve all existing dry runs and delivery boundaries') ON CONFLICT DO NOTHING;
COMMIT;
