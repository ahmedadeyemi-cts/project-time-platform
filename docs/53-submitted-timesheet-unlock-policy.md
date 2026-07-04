# Submitted Timesheet Unlock Policy

## Purpose

This document records the requested lock and unlock behavior for submitted timesheets in Project Pulse.

## Date

2026-06-23

## Business Rule

Once an engineer or project manager submits time, the timesheet should be locked.

If the user needs to correct recently submitted time, they should use an Unlock button.

## Engineer Unlock Rule

A submitted timesheet can be unlocked by the engineer or project manager only if it was submitted within the past two hours.

If the submitted time is older than two hours, the unlock request should be denied and the user should be told to contact their manager.

## User-Facing Message

When the submitted time is older than two hours, the user should see:

```text
This timesheet was submitted more than two hours ago. Please contact your manager to unlock it.
```

## Intended Behavior

1. User submits weekly time.
2. Timesheet status changes to `submitted`.
3. Time cells are locked from direct editing.
4. Unlock button appears for submitted timesheets.
5. If the submission is within two hours, Unlock changes the timesheet back to draft so the user can correct and resubmit.
6. If the submission is older than two hours, Unlock is denied and the user is told to contact their manager.

## Files Added or Updated

- `deployment/rocky-linux/apply-timesheet-unlock-policy-patch.sh`
- `src/frontend/project-time-web/src/timesheet.css`

## API Changes Applied by Patch Script

The patch script updates the backend API source to add:

- `POST /api/timesheets/week/unlock`
- API version `0.3.2`
- `canEdit` flag in the weekly timesheet payload
- `canUnlock` flag in the weekly timesheet payload
- `unlockMessage` in the weekly timesheet payload
- Audit log action `timesheet_engineer_unlocked`

## Frontend Changes Applied by Patch Script

The patch script updates the frontend source to add:

- Unlock button for submitted timesheets
- Unlock API call
- Unlock status messaging
- Submitted timesheets stay locked unless explicitly unlocked

## Validation Steps

1. Pull the latest repository changes.
2. Run the unlock policy patch script.
3. Redeploy the API service.
4. Rebuild the frontend.
5. Restart the local frontend server.
6. Submit a timesheet.
7. Confirm time cells are locked.
8. Click Unlock within two hours of submission.
9. Confirm the timesheet returns to draft/editable state.
10. Submit again.
11. For older submitted records, confirm Unlock displays the manager-contact message.

## Status

Ready for validation.
