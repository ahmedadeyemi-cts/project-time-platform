# 023A Production Data Readiness Center

## Webpage Impact

Adds a clickable Production Data Readiness Center page.

Open:

`https://projectpulse-test.onenecklab.com/#production-data-readiness`

The module is also added to Dashboard/navigation as `Data Readiness`.

## Backend Support

Adds:

`GET /api/production/data-readiness`

The endpoint checks operational data readiness across users, roles, customers, projects, tasks, timesheets, time entries, approvals, exports, audit events, and notification events.

## What to Check on the Webpage

- Data Readiness appears as a clickable module.
- Direct route `#production-data-readiness` loads.
- Refresh data readiness works.
- Cards show endpoint status, ready checks, needs-data count, and missing-table count.
- Table rows explain backend table, count, status, purpose, and what to check.
- Validation links open User Administration, Role Administration, Customer Directory, Project Intake, Project Workspace, Workflow, Manager Approvals, and Audit History.
