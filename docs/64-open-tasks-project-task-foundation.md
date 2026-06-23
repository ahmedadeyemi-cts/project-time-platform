# Open Tasks Project Task Foundation

## Purpose

This document records the next PSA implementation step after the charter gap analysis: connecting assigned project tasks to the engineer timesheet.

## Date

2026-06-23

## Charter Alignment

The USS PSA charter calls for daily time entry per project/task and a PM approval workflow. This update begins that foundation by adding assigned project tasks into the engineer timesheet under `Open tasks`.

## Files Added

- `database/migrations/009_project_task_assignment_foundation_seed.sql`
- `database/rollback/009_project_task_assignment_foundation_seed_rollback.sql`
- `deployment/rocky-linux/apply-migration-009.sh`
- `deployment/rocky-linux/apply-open-tasks-timesheet-patch.sh`

## What Migration 009 Seeds

Migration 009 seeds the following data for validation:

- Engineer: `ahmed.adeyemi@ussignal.com`
- Project Manager: `matthew.lenoble@ussignal.com`
- Client: `US Signal Internal`
- Project: `USS-PSA-2026`
- Project name: `US Signal Professional Services Automation Platform`

It also seeds representative project tasks based on the charter phases:

- Foundation & Infrastructure
- Project Intake & Templates
- Project Management Module
- Resource Scheduling
- Time & Expense Management
- Invoicing & Reporting
- UAT, Training & Go-Live

The engineer is assigned to each task so the Open Tasks activity source has data to display.

## API Added by Patch

- `GET /api/assignments/open-tasks?weekStart=<YYYY-MM-DD>`

The endpoint returns active assigned project tasks for the development engineer within the selected week.

## Frontend Behavior Added by Patch

The Timesheet activity selector now supports:

- Non-project time
- Open tasks
- Regular tasks placeholder
- Requests / Service Requests placeholder

When `Open tasks` is selected, assigned project tasks appear as selectable activity cards. Selecting a task adds it to the timesheet grid as a project-task row.

## Validation Steps

1. Apply migration 009.
2. Apply the Open Tasks timesheet patch.
3. Redeploy the API.
4. Rebuild the frontend.
5. Open the Timesheet section.
6. Change Activity Type to `Open tasks`.
7. Confirm the PSA project tasks appear.
8. Add one project task to the timesheet.
9. Enter time against the project task.
10. Save draft and refresh.
11. Confirm the project task time persists.

## Status

Ready for validation.
