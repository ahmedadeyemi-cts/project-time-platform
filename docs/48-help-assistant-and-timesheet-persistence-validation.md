# Help Assistant and Timesheet Persistence Validation

## Purpose

This document records two follow-up items from user validation:

1. Submitted time entries did not appear after refresh and need database/API validation.
2. Users need a page-level help button where they can ask questions about the workflow they are using.

## Date

2026-06-23

## Help Assistant

A contextual help assistant was added globally to the frontend.

### Files Added

- `src/frontend/project-time-web/src/HelpAssistant.jsx`
- `src/frontend/project-time-web/src/help.css`

### File Updated

- `src/frontend/project-time-web/src/main.jsx`

### Behavior

The Help button appears in the lower-right corner of the application. When opened, users can ask questions about:

- Timesheet entry
- Normal time
- Afterhours / OT
- Saving drafts
- Submit workflow
- Manager approval
- Non-project time categories
- Project-task assignments
- Work locations
- Utilization
- Light/dark mode

This is a local contextual help assistant for the application. It does not yet call an AI service or external knowledge base. It uses known platform guidance embedded in the frontend.

## Submitted Time Persistence Issue

During validation, the user reported that after clicking Submit and refreshing the browser, the entered time did not appear.

This can happen if:

- The latest API service was not rebuilt/redeployed after the persistence update.
- Migration 006 was not applied.
- The frontend is running the latest build but the API is still an older version.
- Submit failed, but the page was refreshed before the error was reviewed.
- The saved data exists in the database but the frontend did not reload it correctly.

## Validation Commands

Run these commands from the OCI VM to confirm the API and database persistence state.

### Confirm API Version

```bash
curl http://127.0.0.1:5080/api/version
```

Expected version:

```text
0.3.0
```

### Confirm Migration 006 Was Applied

```bash
source /opt/project-time-platform/config/postgres.env

PGPASSWORD="$PTP_DB_PASSWORD" psql \
  -h "$PTP_DB_HOST" \
  -p "$PTP_DB_PORT" \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -c "SELECT migration_id, applied_at FROM schema_migrations ORDER BY applied_at;"
```

Expected migration:

```text
006_timesheet_persistence_location_columns
```

### Confirm Timesheets Exist

```bash
PGPASSWORD="$PTP_DB_PASSWORD" psql \
  -h "$PTP_DB_HOST" \
  -p "$PTP_DB_PORT" \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -c "SELECT timesheet_id, user_id, week_start_date, week_end_date, status, submitted_at FROM timesheets ORDER BY updated_at DESC LIMIT 10;"
```

### Confirm Time Entries Exist

```bash
PGPASSWORD="$PTP_DB_PASSWORD" psql \
  -h "$PTP_DB_HOST" \
  -p "$PTP_DB_PORT" \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -c "SELECT te.work_date, te.time_type, te.hours, te.status, npt.category_code, te.description FROM time_entries te LEFT JOIN non_project_time_categories npt ON npt.non_project_time_category_id = te.non_project_time_category_id ORDER BY te.updated_at DESC LIMIT 20;"
```

### Confirm API Returns Saved Entries

```bash
curl 'http://127.0.0.1:5080/api/timesheets/week?weekStart=2026-06-21'
```

The response should include an `entries` array. If the database has saved entries but the API does not return them, the backend read logic needs to be reviewed. If the database does not have saved entries, the submit call did not persist data.

## Required Redeploy After Pull

After pulling this update, rebuild and redeploy:

1. Apply migration 006 if not already applied.
2. Reinstall the API systemd service.
3. Rebuild the frontend.
4. Restart the local frontend server.
5. Hard refresh the browser.

## Status

Ready for validation.
