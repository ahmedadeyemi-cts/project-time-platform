# Timesheet Persistence Phase

## Purpose

This document records the first backend persistence phase for the Project Time Platform weekly timesheet workflow.

## Date

2026-06-23

## Scope

This phase connects the interactive React timesheet page to PostgreSQL-backed API endpoints so weekly time entries can be saved as drafts and submitted for manager approval.

## What Was Added

### Database Migration

A new migration was added:

- `database/migrations/006_timesheet_persistence_location_columns.sql`

The migration adds these columns to `time_entries`:

- `work_location_group_id`
- `work_location_id`

These support the requirement that time entries can capture work location detail. This is still foundation-level support. The future engineer onboarding workflow must set engineer-specific default work location and timezone values.

### Rollback Script

A rollback script was added:

- `database/rollback/006_timesheet_persistence_location_columns_rollback.sql`

### Apply Script

A deployment script was added:

- `deployment/rocky-linux/apply-migration-006.sh`

This script:

1. Loads PostgreSQL connection settings from `/opt/project-time-platform/config/postgres.env`.
2. Checks whether migration `006_timesheet_persistence_location_columns` has already been applied.
3. Applies the migration only if needed.
4. Validates the new `time_entries` location columns.

### API Endpoints

The backend API was updated to version `0.3.0`.

The following endpoint was enhanced:

- `GET /api/timesheets/week?weekStart=<YYYY-MM-DD>`

It now returns saved timesheet header and saved time entries when available.

The following endpoints were added:

- `POST /api/timesheets/week/draft`
- `POST /api/timesheets/week/submit`

### Draft Save Behavior

Draft save currently:

- Creates a development user if one does not exist.
- Creates or updates the weekly timesheet as `draft`.
- Replaces the saved time entries for that timesheet with the submitted payload.
- Stores normal and afterhours entries.
- Stores non-project category, work date, hours, comment, work location group, and work location.
- Writes an audit log entry.

### Submit Behavior

Submit currently:

- Validates that at least one time entry has hours greater than zero.
- Saves the current submitted payload as time entries.
- Marks the weekly timesheet as `submitted`.
- Marks saved time entries as `submitted`.
- Writes an audit log entry.
- Returns the refreshed timesheet payload.

This phase does not yet create manager approval decision records because the current `approval_records` table is structured for completed approval decisions rather than pending approval queue items.

## Frontend Updates

The frontend timesheet page now includes:

- `Save draft` button.
- Real submit call to the backend API.
- Saved draft entries loaded from the database on page refresh.
- Submitted timesheet status reflected in the UI.
- Basic edit lock once a timesheet is submitted.

## Current Development Identity

Until Microsoft Entra ID authentication is connected, the API uses a temporary development user:

- Display name: `Ahmed Adeyemi`
- Email: `ahmed.adeyemi@ussignal.local`

This is a development placeholder only and must be replaced by authenticated Entra user identity before production use.

## Current Limitations

This phase does not yet include:

- Microsoft Entra identity binding.
- Engineer-specific default location and timezone from onboarding/profile data.
- Project-task assignment rows in the timesheet UI.
- Manager approval queue table or workflow records.
- Project manager approval screen.
- Accounting reconciliation screen.
- Reopen/decline workflow after submission.

## Validation Steps

1. Pull the latest repository changes on the OCI VM.
2. Apply migration 006.
3. Reinstall/redeploy the API service.
4. Rebuild the frontend.
5. Restart the local frontend server.
6. Open the app through `http://127.0.0.1:5173/` using the SSH tunnel.
7. Enter time into one or more timesheet cells.
8. Click `Save draft`.
9. Refresh the browser and confirm the saved draft time remains visible.
10. Click `Submit`.
11. Confirm the timesheet status changes to submitted for manager approval.
12. Refresh again and confirm the submitted time remains visible.
13. Confirm submitted rows are locked from further editing.

## Recommended Next Phase

The next recommended phase is the Manager Approval foundation:

- Add a pending approval queue or status-based manager approval endpoint.
- Build manager approval screen.
- Support approve/decline at the time-entry or timesheet level.
- Record approval decisions in `approval_records`.
- Allow declined timesheets to return to the engineer for correction.

## Status

Ready for validation.
