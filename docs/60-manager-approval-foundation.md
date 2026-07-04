# Manager Approval Foundation

## Purpose

This document records the first manager approval workflow foundation for Project Pulse.

## Date

2026-06-23

## Scope

This phase adds a manager-facing approval queue for submitted day-level time entries.

## Business Workflow

1. Engineer enters time for a day.
2. Engineer submits that day once it reaches at least 8.00 hours.
3. The submitted day appears in the Manager Approval queue.
4. Manager reviews the submitted day.
5. Manager can approve, decline/return, or unlock the day.

## Manager Actions

### Approve

Manager approval updates the submitted day to `manager_approved` and updates related time entries to `manager_approved`.

### Decline / Return

Manager decline requires a reason. The day is updated to `manager_declined`, and the engineer can correct the returned day and resubmit.

### Unlock

Manager unlock changes the day back to draft so the engineer can correct the submitted time outside the engineer self-unlock window.

## Database Update

Migration 008 adds manager decision metadata to `timesheet_day_statuses`:

- `manager_user_id`
- `manager_decision_comment`
- `manager_approved_at`
- `manager_declined_at`
- `manager_unlocked_at`

## Files Added or Updated

- `database/migrations/008_manager_approval_day_fields.sql`
- `database/rollback/008_manager_approval_day_fields_rollback.sql`
- `deployment/rocky-linux/apply-migration-008.sh`
- `deployment/rocky-linux/apply-manager-approval-api-patch.sh`
- `src/frontend/project-time-web/src/ManagerApprovalPanel.jsx`
- `src/frontend/project-time-web/src/manager-approval.css`
- `src/frontend/project-time-web/src/main.jsx`

## API Endpoints Added by Patch Script

- `GET /api/manager/approvals?weekStart=<YYYY-MM-DD>&includeAll=false`
- `POST /api/manager/approvals/approve`
- `POST /api/manager/approvals/decline`
- `POST /api/manager/approvals/unlock`

## Development Manager Identity

Until Microsoft Entra ID and real role-based identity are connected, manager actions use a temporary development manager account:

```text
Development Manager
manager@ussignal.local
```

## Validation Steps

1. Apply migration 008.
2. Run the manager approval API patch script.
3. Redeploy the API.
4. Rebuild the frontend.
5. Restart the local frontend server.
6. Submit a day from the engineer timesheet.
7. Scroll to the Manager Approval panel.
8. Confirm the submitted day appears.
9. Approve the submitted day.
10. Confirm it disappears from the pending-only view.
11. Toggle Show all and confirm the approved day is visible.
12. Submit another day.
13. Decline it with a reason.
14. Confirm it returns to the engineer workflow for correction.
15. Use Manager unlock to reopen a submitted day when appropriate.

## Current Limitations

This phase does not yet include:

- Microsoft Entra role-based manager identity.
- Filtering manager approvals by direct reports only.
- Email or Teams notifications.
- Project manager approval after manager approval.
- Accounting reconciliation workflow after project manager approval.

## Status

Ready for validation.
