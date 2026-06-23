# Approval Inbox Notifications and Bulk Approval

## Purpose

This document records the Approval Inbox update for Project Pulse.

## Date

2026-06-23

## Design Direction

Manager approval should be treated as a dedicated Approval Inbox rather than being buried in the dashboard. Managers and project managers may have multiple engineers, projects, and approvals to review, so the application needs a focused approval workspace.

## Updates Added

### Approval Notification

A notification banner was added. When the logged-in manager has pending approvals, the banner appears and links directly to the Approval Inbox.

### Approval Inbox

The existing Manager Approval section is being positioned as the Approval Inbox. It will support manager approvals now and can later include project manager approvals as the next workflow stage.

### Bulk Approval

Bulk approval support was added for manager approval items.

Managers can:

1. Select individual submitted days.
2. Select all pending items for the current week.
3. Approve selected items in one action.

### API Additions

The API patch adds:

- `GET /api/manager/approval-summary`
- `POST /api/manager/approvals/bulk-approve`

## Files Added or Updated

- `src/frontend/project-time-web/src/ApprovalNotificationBanner.jsx`
- `src/frontend/project-time-web/src/approval-notification.css`
- `src/frontend/project-time-web/src/main.jsx`
- `deployment/rocky-linux/apply-approval-inbox-bulk-ui-patch.sh`
- `deployment/rocky-linux/apply-approval-inbox-bulk-api-patch.sh`

## Current Scope

This update supports manager approval bulk actions. Project manager approval counts are currently returned as `0` until the project manager approval workflow is built.

## Future Scope

The same Approval Inbox should later include:

- Manager Approval
- Project Manager Approval
- Accounting exceptions
- Returned items requiring action
- Filters by engineer, project, customer, week, status, and approval stage
- Role-based routing after Microsoft Entra ID is connected

## Status

Ready for validation.
