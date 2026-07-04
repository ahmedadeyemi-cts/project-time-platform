# Compact Dashboard and ProjectPulse Database Rename

## Purpose

This document records two requested Project Pulse updates:

1. Reduce the size of the dashboard hero section so the application feels more like a working platform and less like a large landing page.
2. Rename the PostgreSQL database to `ProjectPulse`.

## Date

2026-06-23

## Dashboard Layout Update

The dashboard hero section was reduced by changing:

- Hero panel padding
- Hero border radius
- Main heading size
- Hero body text size
- Status card spacing
- General card shadow intensity

## File Updated

- `src/frontend/project-time-web/src/styles.css`

## Database Rename

A deployment script was added to rename the application database from the current database name to:

```text
ProjectPulse
```

## File Added

- `deployment/rocky-linux/rename-database-to-projectpulse.sh`

## What the Rename Script Does

The script:

1. Loads database settings from `/opt/project-time-platform/config/postgres.env`.
2. Stops the Project Pulse API service to release database connections.
3. Terminates any active sessions to the old database.
4. Renames the PostgreSQL database to `ProjectPulse`.
5. Backs up the current `postgres.env` file.
6. Updates `PTP_DB_NAME=ProjectPulse` in `/opt/project-time-platform/config/postgres.env`.
7. Validates that the application database user can connect to the renamed database.
8. Restarts the API service.

## Important Note

PostgreSQL database names are often lowercase by convention. However, this project is using the requested brand-aligned database name `ProjectPulse`. The script handles the mixed-case name by quoting the database identifier during rename.

## Validation Steps

1. Pull the latest repository changes.
2. Run the database rename script.
3. Confirm the database connection check succeeds.
4. Redeploy or restart the API if needed.
5. Rebuild and restart the frontend.
6. Open Project Pulse in the browser.
7. Confirm the dashboard hero section is smaller.
8. Confirm the Database status card displays `ProjectPulse`.

## Status

Ready for validation.
