# Module 063 — Opportunities & Action Tracker

## Purpose

Module 063 provides a shared opportunity page for Account Executives / Sales,
Presales, Engineers, and Administrators.

Authorized users can:

- create an active opportunity;
- identify the account, topic, owner, revenue, and source opportunity ID;
- add collaborative Sales, Presales, or Engineering tasks;
- assign a task to an eligible user;
- mark tasks completed and preserve who completed each task;
- close an opportunity as Won, Lost, Cancelled, or Other;
- reopen a closed opportunity;
- view active date, closed date, creator, updater, and last-updated timestamps;
- review an immutable activity timeline.

## Route

`#opportunities`

## API

- `GET /api/opportunities/access`
- `GET /api/opportunities/options`
- `GET /api/opportunities`
- `GET /api/opportunities/{opportunityId}`
- `POST /api/opportunities`
- `PATCH /api/opportunities/{opportunityId}`
- `POST /api/opportunities/{opportunityId}/tasks`
- `PATCH /api/opportunities/{opportunityId}/tasks/{taskId}`

## Database

Migration:

`deployment/database/063-module-opportunities-center.sql`

Tables:

- `opportunities`
- `opportunity_tasks`
- `opportunity_events`

## Spreadsheet field mapping

The supplied "AS Opportunities Won This Month" workbook informed these fields:

| Workbook field | Module 063 field |
|---|---|
| Opportunity | `external_opportunity_id` |
| Modified On | `updated_at` / future source-modified metadata |
| Topic | `topic` |
| Account Name | `account_name` or `client_id` |
| Status: Open | `opportunity_status = active` |
| Status: Won | `opportunity_status = closed`, `close_outcome = won` |
| Status: Lost | `opportunity_status = closed`, `close_outcome = lost` |
| Actual Revenue | `actual_revenue` |
| Actual Close Date | `closed_date` |
| Owner | `owner_user_id` when matched, otherwise preserved as account context |

The initial source foundation is manual-entry first. The schema intentionally supports
a later validated XLSX/CRM import without replacing manually maintained records.

## Deployment state

This source phase creates and validates the migration and application source only.
It does not apply the database migration and does not deploy Azure runtime images.

## Frontend route integration correction

Module 063 is registered in the role workspace module list for dashboard-card
and navigation discovery. `getNavigationGroup` places the route under
**Sales & Opportunities**. The `#opportunities` component mount is outside the
legacy dashboard/timesheet structural route boundary, and that legacy content
is excluded while Module 063 is active. Backend Module 063 endpoints remain the
authority for view and write access.
