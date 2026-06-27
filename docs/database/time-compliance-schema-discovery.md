# Time Compliance Schema Discovery

This document captures the current ProjectPulse database tables relevant to 019M-O Time Compliance & Notification Center.

## Relevant tables observed

The current PostgreSQL schema includes 70 public tables. Tables most relevant to time compliance include:

- `app_users`
- `app_roles`
- `app_user_role_assignments`
- `approval_records`
- `audit_logs`
- `company_holidays`
- `email_notification_outbox`
- `notification_group_members`
- `notification_groups`
- `notification_log`
- `notification_outbox`
- `notification_preferences`
- `project_assignments`
- `projects`
- `reminder_rules`
- `reporting_relationships`
- `team_memberships`
- `teams`
- `time_entries`
- `timesheet_day_statuses`
- `timesheets`
- `user_timesheet_preferences`
- `utilization_policies`
- `utilization_policy_targets`
- `utilization_snapshots`
- `utilization_weekly_summaries`

## Time compliance design implication

The notification module should not guess recipients or managers. It should resolve recipients from trusted system tables:

- Engineers from user/role/team/resource tables
- Managers from `reporting_relationships` or equivalent manager relationship table
- Project managers from project/project assignment tables
- Project Team Coordinator from configured notification groups or admin settings
- Holidays from `company_holidays`
- Timesheet compliance from `timesheets`, `time_entries`, `timesheet_day_statuses`, and utilization summary tables

## Safety rules

- Start with dry-run mode enabled.
- Do not send real notifications until the compliance query output is validated.
- All notification sends must be logged.
- Duplicate sends must be prevented by notification type, user, period, and deadline.
- All manager and coordinator copies must be resolved from trusted platform records, not arbitrary user input.
