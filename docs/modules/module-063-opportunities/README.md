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
The workbook was used as a format reference only. No workbook records, customers,
owners, revenue values, or other business content were imported.

## Frontend route integration correction

Module 063 is registered in the role workspace module list for dashboard-card
and navigation discovery. `getNavigationGroup` places the route under
**Sales & Opportunities**. The `#opportunities` component mount is outside the
legacy dashboard/timesheet structural route boundary, and that legacy content
is excluded while Module 063 is active. Backend Module 063 endpoints remain the
authority for view and write access.

## Deployment status

**Status:** Complete — deployed, technically validated, and administrator UAT confirmed.

**Confirmed:** 2026-07-18 UTC

### GitHub checkpoints

- Branch: `feature/module-063-opportunities-center-20260717`
- Module implementation commit: `741ff650e1f13a0736e5e44ac13be4b576095dbc`
- Navigation and route-isolation repair commit: `cbbb75e7778587f7dc628969239e5a9d4ec5c284`

### Azure runtime

- API image: `acrphdtest7825cc.azurecr.io/project-health-dashboard-api@sha256:10185bc58252c768577a343b734a80221ed5949d1b7ad141643bc90556dc43f4`
- API revision: `ca-phd-test-api-westus3--m063api4-0717232631`
- Web image: `acrphdtest7825cc.azurecr.io/project-health-dashboard-web@sha256:a86688c397f45dced8bb1aeabf78878931b9a051d298da814cf483947329feb3`
- Web revision: `ca-phd-test-web-westus3--m063rf2-0718003754`

### Database and data handling

- Migration applied successfully.
- Tables verified: `opportunities`, `opportunity_tasks`, and `opportunity_events`.
- Seeded rows: `0`.
- Spreadsheet usage: format reference only.
- Spreadsheet content imported: `No`.

### Validation evidence

- Public root: HTTP `200`.
- Public health: HTTP `200`.
- Unauthenticated Module 063 access endpoint: HTTP `401`.
- Authenticated administrator access: `canView=true`, `canManage=true`.
- Dashboard card: confirmed visible.
- Navigation group: confirmed under **Sales & Opportunities**.
- Direct route `#opportunities`: confirmed working without legacy dashboard/timesheet overflow.
- Rollback: not required.
- Entra changes: none.

### Preserved modules

The deployment validation preserved Modules `001`, `042`, `057`, `059`, and `060`,
including Timesheet multiview, Certinia, Calendar Capacity, Invoice & Billing,
Session Intelligence, Contracts, Prepaid, Block of Hours, and SELL Quote markers.

### Deployment evidence directory

`/home/ahmed/az12d4/module-063-route-repair-v2-20260718T003754Z`
