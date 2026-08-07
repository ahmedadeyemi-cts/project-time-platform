# Module 001A — Engineer Request Closeout

Module 001A gives Engineers one accountable place to finish their assigned
Service Request, Pre-Sales, and Internal tasks. It is a handoff workflow, not a
replacement for Module 055C: the Engineer confirms their work is complete and
the Project Team Coordinator retains final authority over the original request.

## Roles and scope

- Engineers can view, close, and conditionally reopen only their own eligible
  `project_assignments`.
- Permanent Super Administrators retain diagnostic access under the platform's
  existing actual-session rules.
- Administrator View-As is read-only. A preview session cannot close or reopen
  another user's assignment.
- Module 055C remains the final request and task lifecycle authority for the
  Project Team Coordinator.

The permissions introduced by migration 076 are:

- `VIEW_ENGINEER_TASK_CLOSEOUT_001A`
- `MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A`

## Supported work

The backend derives eligibility from authoritative project and task data. The
workspace recognizes Service Requests, Pre-Sales work, and Internal work by
normalized `projects.work_type`, the established `SR-`, `PRES-`, and `INT-`
project-code conventions, or existing service-request task metadata.

The Active tab shows current assigned work with customer, request/project, task,
coordinator, assigned hours, recorded hours, and remaining hours. The Historical
tab shows Engineer closures, Module 055C final closures, notification state, and
immutable transition evidence.

## Close workflow

1. The Engineer enters a completion summary and closes the assigned task in
   Module 001A.
2. The closeout row and immutable event are written in the same transaction as
   the Module 065 notification dispatch and its server-derived recipients.
3. The assignment is marked `engineer_closed`, and active Module 001 weekly task
   lines for that assignment are deactivated.
4. Module 001 task pickers, the work queue, and timer/task target resolution stop
   returning the assignment.
5. A database trigger blocks new time, moving time onto the closed assignment,
   or increasing recorded hours. Existing time remains available for approval,
   audit, rejection correction that does not increase hours, and accounting.
6. Module 065 sends the notice to the project's Project Team Coordinator (or the
   active PTC role fallback) and CCs the assigned Engineer.
7. The Project Team Coordinator reviews and finally closes the original request
   in Module 055C.

## Reopen workflow

An Engineer may reopen an Engineer-closed task only while both of these
authoritative conditions remain true:

- the project is not in a terminal Module 055C lifecycle status; and
- the original project task remains active.

The Engineer must enter a specific reason of at least ten characters. Reopening
reactivates the assignment for Module 001 selection, preserves every prior event,
and creates a new immutable event plus a Module 065 email to the PTC with the
Engineer CC'd. The reason is included in that email.

When Module 055C closes the project or deactivates the original task, migration
076 projects `ptc_final_closed` into Module 001A, writes final-close evidence,
and permanently disables Engineer reopen for that closeout.

## API contract

- `GET /api/engineer-task-closeout/overview`
- `POST /api/engineer-task-closeout/assignments/{assignmentId}/close`
  with `{ "completionSummary": "..." }`
- `POST /api/engineer-task-closeout/assignments/{assignmentId}/reopen`
  with `{ "reason": "..." }`

All assignment, Engineer, coordinator, billing-lock, and recipient values are
resolved on the server. The browser cannot choose another Engineer or arbitrary
email recipients.

## Data and rollback

Migration `076_module_001a_engineer_request_closeout.sql` adds the assignment
projection, closeout state, immutable event history, final-close projections,
billing guard, RBAC permission grants, and feature-catalog registration.

The guarded rollback refuses to run after any closeout or event evidence exists.
This prevents a rollback from silently deleting business and notification
history. No migration or deployment is performed by the Module 001A source PR.
