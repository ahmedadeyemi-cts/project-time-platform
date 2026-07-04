# Daily Timesheet Submission Policy

## Purpose

This document records the change from a fully weekly submit model to a daily submit model for Project Health Dashboard.

## Date

2026-06-23

## Business Rules

Each engineer should enter at least 8.00 hours per work day before submitting that day.

Submitting time should lock the submitted day, not the entire week. This allows engineers and project managers to continue entering time for other days in the same week.

Engineers should be able to add other available project tasks, regular tasks, service requests, or non-project activities after one day has already been submitted, as long as they are adding time to days that are still open.

## Daily Submit Rule

A day can be submitted only when the total time for that day is at least 8.00 hours.

Examples:

- 8.00 normal hours = valid.
- 7.50 normal hours + 0.50 afterhours = valid.
- 4.00 project hours + 4.00 non-project hours = valid.
- 6.00 total hours = not valid.

## Daily Lock Rule

Once a day is submitted, only that day is locked. Other days remain editable.

## Daily Unlock Rule

A submitted day can be unlocked by the engineer only if it was submitted within the past two hours.

If the submitted day is older than two hours, the engineer should see a message instructing them to contact their manager.

## Database Update

Migration 007 adds a new table:

- `timesheet_day_statuses`

This table tracks the status of each submitted work date independently from the weekly timesheet header.

## Files Added

- `database/migrations/007_timesheet_day_submission_status.sql`
- `database/rollback/007_timesheet_day_submission_status_rollback.sql`
- `deployment/rocky-linux/apply-migration-007.sh`
- `deployment/rocky-linux/apply-daily-submission-policy-patch.sh`

## API Changes Applied by Patch Script

The daily submission patch adds:

- `POST /api/timesheets/day/submit`
- `POST /api/timesheets/day/unlock`
- Daily 8-hour minimum validation
- Daily submit lock behavior
- Daily unlock within two hours
- Daily status payload on weekly timesheet load

## Frontend Changes Applied by Patch Script

The frontend patch adds day-level actions from the time-entry modal:

- Submit this day
- Unlock this day
- Day total display
- Minimum 8-hour validation messaging

## Validation Steps

1. Apply migration 007.
2. Run the daily submission patch script.
3. Redeploy the API.
4. Rebuild the frontend.
5. Restart the frontend server.
6. Enter fewer than 8 hours for a day and try to submit that day.
7. Confirm the system blocks submission.
8. Enter 8 or more hours for the day.
9. Submit that day.
10. Confirm only that day locks.
11. Enter time on a different day.
12. Confirm the different day remains editable.
13. Add another activity row after one day has been submitted.
14. Confirm the new row can be used for open days.
15. Unlock the submitted day within two hours.
16. Confirm the day becomes editable again.

## Status

Ready for validation.
