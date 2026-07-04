# Project Pulse Personalization, Holidays, and Reminder Package

## Purpose

This package adds the foundation for hyper-personalized timesheets, annual holiday uploads, and reminder scheduling.

## Key Decisions

- No global default rows should appear on a timesheet.
- Default rows are controlled per individual user.
- Vacation is used for PTO.
- Holiday is used only for company-paid holidays and floating holidays.
- All resources are required to submit 40 hours of time each week.
- If PTO is planned near a deadline, time should be submitted before the resource is out.
- Company holidays can be uploaded each year.
- Holiday rows can auto-populate 8.00 hours for users who have holiday auto-add enabled.

## Files Added

```text
database/migrations/012_personalized_timesheet_holidays_reminders.sql
database/rollback/012_personalized_timesheet_holidays_reminders_rollback.sql
deployment/rocky-linux/apply-migration-012.sh
deployment/rocky-linux/apply-personalization-holidays-api-patch.sh
deployment/rocky-linux/apply-personalized-timesheet-ui-patch.sh
deployment/rocky-linux/import-company-holidays.py
deployment/rocky-linux/project-pulse-reminder-scheduler.sh
deployment/rocky-linux/install-reminder-timers.sh
```

## Run Order

```bash
cd /opt/project-time-platform/app/project-time-platform

GIT_SSH_COMMAND='ssh -i ~/.ssh/github_project_time_platform -o IdentitiesOnly=yes' \
git pull

chmod +x deployment/rocky-linux/apply-migration-012.sh
./deployment/rocky-linux/apply-migration-012.sh

chmod +x deployment/rocky-linux/apply-personalization-holidays-api-patch.sh
./deployment/rocky-linux/apply-personalization-holidays-api-patch.sh

chmod +x deployment/rocky-linux/apply-personalized-timesheet-ui-patch.sh
./deployment/rocky-linux/apply-personalized-timesheet-ui-patch.sh

chmod +x deployment/rocky-linux/install-reminder-timers.sh
./deployment/rocky-linux/install-reminder-timers.sh

chmod +x deployment/rocky-linux/install-api-systemd-service.sh
./deployment/rocky-linux/install-api-systemd-service.sh

chmod +x deployment/rocky-linux/build-frontend.sh
./deployment/rocky-linux/build-frontend.sh

sudo systemctl restart projecttime-frontend-public.service
```

## API Endpoints Added

```text
GET  /api/users/timesheet-preferences
POST /api/users/timesheet-preferences
GET  /api/holidays?year=YYYY
POST /api/reminders/queue-weekly-engineer
POST /api/reminders/queue-month-end-pm
GET  /api/reminders/outbox?limit=10
```

## Holiday Import Format

CSV columns:

```text
holiday_date,holiday_name,holiday_type,is_floating_holiday,auto_populate_hours
```

Example:

```text
2026-01-01,New Year's Day,company_paid,false,8
2026-07-03,Floating Holiday,floating,true,8
```

Import command:

```bash
python3 deployment/rocky-linux/import-company-holidays.py 2026 /path/to/holidays-2026.csv ahmed.adeyemi@ussignal.com
```

## Reminder Timers

The installer creates:

```text
project-pulse-weekly-engineer-reminder.timer
project-pulse-month-end-pm-reminder.timer
```

Behavior:

- Weekly engineer reminder check runs every Friday at 09:00.
- Month-end PM reminder check runs every Friday at 09:05.
- Month-end message is queued only when that Friday is the last Friday of the month.

## Note About Email Sending

This package creates the notification rules, recipient groups, timers, and outbox. Actual SMTP delivery requires SMTP details to be configured in the next step. Until SMTP is configured, reminders are queued in the outbox for validation.

## Expected API Version

```text
0.5.3
```
