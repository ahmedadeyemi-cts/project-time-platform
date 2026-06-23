# Timesheet Page V1 Validation Plan

## Purpose

This document describes the first interactive frontend build of the Project Time Platform weekly timesheet page.

## Implementation Date

2026-06-23

## Scope

This frontend phase moves the application beyond the dashboard shell and introduces a ChangePoint-inspired weekly time entry experience.

Included in this phase:

- Weekly navigation with previous, current week, and next week controls.
- Current weekly date range display.
- Non-project activity panel populated from the API.
- Default non-project rows for Administrative and Peer Support when available.
- Ability to add other non-project categories into the grid.
- Ability to remove active rows from the grid.
- Daily entry cells with separate Normal and Afterhours inputs.
- Row totals, day totals, normal total, afterhours total, and grand total calculations.
- Details panel for selected time cells.
- Per-cell comment/description entry.
- Work location group and work location dropdowns populated from API data.
- Reset action for clearing local entries.
- Submit action that marks the local draft as submitted for manager approval.

## Current Limitations

This is still a frontend foundation phase. The following items are intentionally not complete yet:

- Time entries are not persisted to PostgreSQL yet.
- Submit does not yet create approval workflow records in the database.
- Project task rows are not populated yet because saved project, task, and assignment data still need to be seeded and exposed through API endpoints.
- Manager approval, project manager approval, and accounting reconciliation screens are not yet implemented.

## Files Updated

- `src/frontend/project-time-web/src/App.jsx`
- `src/frontend/project-time-web/src/timesheet.css`

## API Dependencies

The page currently consumes these existing endpoints:

- `/health`
- `/api/db-health`
- `/api/schema/tables`
- `/api/timesheets/week?weekStart=<YYYY-MM-DD>`
- `/api/work-location-groups`
- `/api/work-locations`
- `/api/utilization/policies`
- `/api/utilization/targets`

## Validation Steps

1. Pull the latest repository updates on the OCI VM.
2. Rebuild the React frontend.
3. Restart the local frontend server.
4. Open `http://127.0.0.1:5173/` through the SSH tunnel.
5. Confirm the dashboard still loads.
6. Scroll to the Timesheet section.
7. Confirm Previous, Current week, and Next controls update the visible week.
8. Confirm Administrative and Peer Support appear by default when loaded from API data.
9. Add another non-project category from the activity panel.
10. Enter normal and afterhours values into several day cells.
11. Confirm row totals, day totals, normal total, afterhours total, and grand total update.
12. Select a time cell and enter a comment in the Details panel.
13. Select a work location group and work location.
14. Click Submit and confirm the status changes to submitted for manager approval.
15. Click Reset and confirm local entries are cleared.
16. Toggle light/dark mode and confirm the timesheet remains usable.

## Expected Result

The weekly timesheet page should behave like an interactive working prototype and support the primary time entry user experience needed before persistence and approval workflow are connected.

## Next Recommended Build Phase

The next phase should add backend persistence for draft time entries and submitted timesheets. That phase should include:

- API endpoint for saving draft time entries.
- API endpoint for submitting a weekly timesheet.
- Database insert/update logic for `timesheets` and `time_entries`.
- Status updates from draft to submitted.
- Basic validation rules for hours, comments, time type, non-project category, project task, and work location.

## Status

Ready for user validation.
