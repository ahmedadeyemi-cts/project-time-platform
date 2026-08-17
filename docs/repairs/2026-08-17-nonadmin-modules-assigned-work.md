# Non-Super-Administrator Modules and Assigned-Work Runtime Repair

## Scope

This repair addresses two independent production symptoms without changing Production infrastructure, authentication providers, secrets, certificates, DNS, or email configuration.

1. Authorized modules for non-Super-Administrator users must appear immediately when the user opens **Modules**.
2. Service Requests, Presales Tasks, and Internal Tasks assigned in Module 055C must become visible through the shared canonical assignment model consumed by:
   - Module 019 — Project Engineering Workspace
   - Module 001A — Engineer Request Closeout
   - Module 001 — Timesheet

## Root causes addressed

### Modules directory

The directory could enter an empty loading state while the full dynamic RBAC evidence set was still resolving. Optional module-owner metadata and dashboard-only administrative calls could also generate 503 or 403 responses for roles that were never expected to use those features.

The repair now:

- Publishes an immediate, provisional module-directory authority snapshot from the already authorized sidebar navigation.
- Uses a same-identity session cache only when the live navigation has not yet completed.
- Replaces the provisional snapshot when the server-authoritative RBAC response becomes ready.
- Prevents ordinary roles from requesting module-owner administration.
- Prevents role-inapplicable dashboard requests from being sent to protected endpoints.
- Leaves every backend authorization check intact.

### Assigned Service Request, Presales, and Internal work

Module 055C assignment history may contain either a canonical task UUID or a durable task code/identifier. Migration 092 handled UUID references only. When a task code was stored, no canonical `project_assignments` row was created, so Modules 019, 001A, and 001 could not see the assignment.

Migration 093 now:

- Resolves task UUIDs and durable task codes within the assigned project.
- Synchronizes active Module 055C assignment history into `project_assignments`.
- Backfills existing active assignments.
- Re-synchronizes when a task is created, its code changes, it becomes inactive, or the project enters a terminal status.
- Closes only Work Register bridge-owned canonical rows when the corresponding assignment is no longer active.
- Does not hard-code a work item, engineer, customer, email address, or environment.

## Test acceptance

### Non-Super-Administrator Modules page

1. Sign in as an Engineer.
2. Open **Modules**.
3. Authorized modules must appear immediately; the user must not need to press **Show authorized modules**.
4. Network activity must not contain a request to `/api/module-catalog/owners`.
5. Network activity must not contain role-inapplicable 403 requests for:
   - production readiness command center
   - navigation registry integrity
   - module visibility smoke
   - audit summary
   - approval/export summary
   - production acknowledgment summary
   - manager approvals
   - PTC timesheet-steward users
6. Repeat with Engineering Lead, Manager, Director/Executive, Project Manager, Accounting, and Project Team Coordinator roles.
7. Verify that each role sees only its already authorized modules.
8. Sign in as the actual Super Administrator and confirm owner metadata and owner changes still load normally.

### Assigned-work visibility

Use one active example of each work type:

- Service Request
- Presales Task
- Internal Task

For every example:

1. Create the work item in Module 055D.
2. Manage the task and assign an Engineer in Module 055C.
3. Verify the assigned Engineer sees it in Module 019.
4. Verify the assigned Engineer sees eligible Service Request, Presales, or Internal work in Module 001A.
5. Verify the assigned Engineer sees it in the Module 001 Timesheet work queue and can add it to the week.
6. Verify another Engineer does not see the assignment.
7. Reassign the task and confirm the old Engineer loses access while the new Engineer gains access.
8. Close or deactivate the task and confirm it is removed from active Module 019, Module 001A, and Module 001 views according to their existing lifecycle rules.

The reported Service Request `SR-8C81ACA3` is a valid UAT example, but the implementation does not contain or depend upon that identifier.
