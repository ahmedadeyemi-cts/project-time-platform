# Module 002 — Approval Center

Module 002 provides a role-scoped approval experience for submitted time and local administrator password resets.

## Role contract

| Role | Time approvals | Password reset approvals | Scope |
|---|---:|---:|---|
| Manager | Yes | Yes | Direct reports |
| Project Manager / Project Management | Yes | No | Projects they manage |
| Project Team Coordinator | Yes | Yes | Organization-wide |
| Administrator | Yes | Yes | Organization-wide |
| Super Administrator | Yes | Yes | Organization-wide |
| All other roles | No | No | None |

The API derives roles and scope from the authenticated ProjectPulse session. Browser-supplied role or scope values are not trusted.

## Actionable count contract

Only submitted time and active local password-reset work count as actionable. Draft, approved, returned, completed, cancelled, resolved, and expired records do not count.

A compact mailbox control in the authenticated top bar displays a red count badge when the signed-in user has actionable work. The previous floating approval banner is removed.

## Rejection notification

A time rejection requires a specific reason. The transaction preserves the approval decision and audit event, then queues detailed engineer notifications in both `notification_outbox` and `email_notification_outbox` for Global SMTP processing. The message lists the work date, reviewer, entry identifiers, project or category context, hours, submitted descriptions, and required correction.

## Administrative stale-item resolution

Administrators and Super Administrators may resolve submitted approval items that are at least seven days old. The resolution is an audited return-to-engineer action; it does not delete approval history.

## Source validation

The frontend build runs `npm run validate:module002`. The validator enforces the mailbox, role contract, route isolation, draft exclusion, password-reset separation, rejection email contract, and removal of browser prompts and the floating banner.

This source phase does not alter the database schema, Entra configuration, or Azure runtime.
