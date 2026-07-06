# ProjectPulse 051B Time Entry Critical Repair

## Covered

- PP-C3: Engineer cannot rewrite days once submitted, manager-approved, PM-approved, accounting-ready, reconciled, or locked.
- PP-C4: Engineer draft-save path remains available for draft and manager-declined time.
- PP-C5: Day-submit frontend no longer references undefined `dayEntries`.
- PP-H1: Eligible Engineer and Project Manager users automatically receive submitted company holiday entries when their timesheet week loads.

## Holiday auto-submit behavior

When an active, non-floating company holiday falls on a weekday in the selected week:

- Eligible roles: ENGINEER, ENGINEERING, PROJECT_MANAGER, PROJECT_MANAGEMENT, PM_TEAM_LEAD, PROJECT_MANAGEMENT_LEAD.
- Uses `company_holidays.auto_populate_hours`, defaulting to 8.00 only when the source value is invalid.
- Writes a HOLIDAY non-project time entry with status `submitted`.
- Marks the corresponding `timesheet_day_statuses` row as submitted.
- Records audit action `timesheet_holiday_auto_submitted`.
- Skips days already submitted, approved, accounting-ready, reconciled, or locked.
- Skips days with existing non-holiday manual work.

## Required browser validation

1. Sign in as an Engineer or PM.
2. Open Time Entry for a week containing an uploaded company holiday.
3. Confirm the holiday appears as submitted.
4. Confirm the holiday cannot be manually overwritten after submission.
5. Confirm Save draft still works for a non-holiday draft day.
6. Confirm Submit this day works and no undefined variable error appears.
7. As a manager/admin, approve a day and confirm the engineer cannot rewrite it.
