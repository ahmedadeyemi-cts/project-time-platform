# Module 030 — Analytics Center Enterprise Experience

## Purpose

The Analytics Center is the enterprise reporting and recurring-delivery experience for ProjectPulse. It is designed for technical and nontechnical users and replaces the former dense, command-center-style workflow with one understandable process:

1. Select a report from a searchable, collapsible library.
2. Show only the criteria that apply to that report.
3. Select one or many authorized Customers, Projects, Engineers, Project Managers, Teams, and contract types where applicable.
4. Preview current role-scoped results.
5. Run and save immutable execution evidence.
6. Export an official US Signal PDF or branded Excel workbook.
7. Schedule individualized recurring delivery through Module 065.

The page also includes an enterprise default dashboard, recent and favorite reports, KPI views, Data Explorer, source-quality evidence, subscription and delivery status, and schedule administration.

## User experience

### Official US Signal navigation and branding

The page uses the approved `USSignalLogo` component and current US Signal navy, blue, cyan, green, warning, and critical presentation. It includes:

- Analytics Center navigation;
- Back to Modules;
- Back to Dashboard;
- Home;
- Dashboards;
- Reports;
- Schedules;
- Data Explorer;
- KPIs & Metrics;
- Alerts & Subscriptions;
- Data Quality; and
- Admin.

The nested Analytics navigation changes only the Module 030 workspace. It does not replace application-level permission or route security.

### Default dashboard

The default page presents truthful, role-scoped metrics rather than sample operational data:

- visible contracted value;
- active projects;
- billable utilization;
- hours used in the current visible scope;
- forecast variance;
- customers with visible projects beginning in the current year;
- Project Manager workload; and
- recurring-report delivery health.

A metric returns `Not available` when its authorized source or financial field is unavailable. The interface does not invent a trend or comparison.

### Recent reports and favorites

Recent and favorite report activity is stored per actual user. A favorite does not grant access to the report or its records. The current catalog and report authorization are re-evaluated every time a report is opened or run.

### Collapsible report library

The 24-report catalog remains the authoritative reporting surface. Categories are collapsible, searchable, and show only reports available to the current role.

The selected report displays:

- Criteria;
- Schedules; and
- About this Report.

Only the filters defined by that report appear.

## Multiple-selection criteria

The enterprise experience adds checkbox and chip-based multiple selection for:

- Customers;
- Projects;
- Engineers;
- Project Managers;
- Teams; and
- contract types.

Customer, Project, and Team choices cause server-side cascading option refreshes. Filter options are returned from the authorized Customer Directory, project portfolio, assignments, project ownership, and team membership sources.

The interface retains the defaults:

- All customers;
- All projects;
- All engineers;
- All Project Managers;
- All teams; and
- All contract types.

### Modules 055C and 055D contract types

Canonical values are:

- Fixed Price
- Time and Material
- Pre-Sales
- Internal
- Non-billable
- Other

Known aliases are normalized before scope filtering.

## Role enforcement

### Engineers

Engineer-only sessions are locked on the server to the effective Engineer for person-level time, workload, utilization, assignment, task, and project evidence. Selecting another Engineer identifier cannot expand the report.

### Project Managers

Project Manager sessions are locked on the server to projects managed by the effective PM or another currently authorized PM relationship. Selecting another PM, Customer, Project, Engineer, or Team cannot expand the portfolio.

### Broader roles

Accounting, Billing, Finance, Project Team Coordinator, Executive, Manager, Sales, Solution Architect, Administrator, and Super Administrator visibility remains governed by existing record and financial-field permissions.

Analytics permissions do not create new customer, project, employee, or sensitive financial access.

### View-As

View-As can preview current effective-user results but cannot:

- persist a report run;
- export a report;
- save a recurring schedule;
- run a schedule;
- change recent/favorite activity; or
- send email.

## Reports and exports

Every persisted report can be exported as:

- official US Signal branded PDF;
- official US Signal branded Excel;
- CSV; or
- JSON.

### PDF

The PDF is generated inside the ProjectTime API and includes:

- the approved embedded US Signal logo;
- Analytics Center identity;
- report name;
- run ID;
- result status;
- row count;
- effective criteria;
- paginated role-scoped results;
- generation timestamp; and
- page footer.

### Excel

The workbook includes:

1. **Report** — official logo, report/run metadata, frozen/filterable result columns, print setup, and US Signal footer.
2. **Criteria** — effective filters recorded for the run.
3. **Sources** — source status, required/optional state, records, timestamp, diagnostic code, and message.

Export format, row count, and SHA-256 evidence remain immutable.

## Recurring schedules

Users with current run authority can create schedules. Supported cadences are:

- Daily
- Weekdays
- Weekly
- Monthly
- Quarterly
- Yearly

A schedule records:

- report;
- criteria;
- cadence;
- day/month selections;
- local time;
- timezone;
- enabled state;
- US Signal PDF or Excel format;
- delivery boundary;
- email subject and message; and
- one or more recipients.

### Multiple recipients and individualized scope

Authorized schedule managers can select multiple active ProjectPulse users. Each active user receives an individual report generated under that recipient's own current role and record scope.

Roles without multiple-recipient delivery authority can schedule only their own active ProjectPulse email.

Governed manual recipients must use an `@ussignal.com` address and require `DELIVER_ANALYTICS_SCHEDULES` authority. Manual addresses receive the schedule owner's current role-scoped result and therefore remain restricted to operationally accountable roles.

### Module 065 ownership

Module 065 remains the only owner of:

- Entra Secret Administration;
- Microsoft Graph configuration;
- Microsoft 365 SMTP configuration;
- Test and Production credentials;
- SMTP host and port;
- sender mailbox and Reply-To;
- provider selection;
- recipient delivery boundary;
- transport readiness; and
- external email transmission.

The Analytics Center creates the branded attachment and asks Module 065 to deliver it. It does not create another provider, credential, sender, or SMTP configuration.

### Scheduler safety

The bounded scheduler:

- starts with the API;
- uses a PostgreSQL advisory lock so one replica processes due schedules;
- processes a bounded number per cycle;
- re-evaluates recipient authorization;
- generates one report per active ProjectPulse recipient;
- records immutable run and delivery evidence;
- updates the next run time; and
- fails closed when migration, database, authorization, export, Module 065, or provider readiness is unavailable.

## Migration 060

`060_analytics_center_enterprise_experience` creates:

- `analytics_report_schedules`
- `analytics_report_schedule_recipients`
- `analytics_report_schedule_runs`
- `analytics_report_schedule_delivery_attempts`
- `analytics_user_report_activity`

It also permits `pdf` in `enterprise_report_exports` and adds:

- `VIEW_ANALYTICS_DASHBOARDS`
- `VIEW_ANALYTICS_SCHEDULES`
- `MANAGE_ANALYTICS_SCHEDULES`
- `DELIVER_ANALYTICS_SCHEDULES`

Schedule-run and delivery-attempt evidence is immutable. The rollback removes only migration-060-owned tables, permissions, triggers, and PDF evidence support, and restores the migration-055 export-format constraint.

Migration 060 is source only until separately authorized for a connected environment.

## API

```text
GET  /api/analytics/v2/overview
GET  /api/analytics/v2/catalog
POST /api/analytics/v2/filter-options
POST /api/analytics/v2/preview
POST /api/analytics/v2/run
GET  /api/analytics/v2/history
GET  /api/analytics/v2/runs/{runId}/export?format=pdf|xlsx|csv|json
POST /api/analytics/v2/activity/{reportCode}/view
PUT  /api/analytics/v2/activity/{reportCode}/favorite
GET  /api/analytics/v2/recipient-options
GET  /api/analytics/v2/schedules
POST /api/analytics/v2/schedules
PUT  /api/analytics/v2/schedules/{scheduleId}
DELETE /api/analytics/v2/schedules/{scheduleId}
POST /api/analytics/v2/schedules/{scheduleId}/run-now
GET  /api/analytics/v2/schedule-runs
GET  /api/analytics/v2/schedules/readiness
POST /api/analytics/v2/schedules/run-due
```

The migration-055 `/api/analytics` surface remains available for compatibility. The enterprise interface uses `/api/analytics/v2`.

## Source isolation and diagnostics

One unavailable optional source does not blank healthy report rows. Results continue to distinguish:

- complete;
- partial;
- no data;
- source unavailable; and
- failed.

Friendly messages are separated from source keys, observation timestamps, sanitized diagnostic codes, correlation IDs, and required/optional evidence.

## Shared integration

The API endpoint registration is applied after the canonical generated Program registration through `ProjectTime.Api/Directory.Build.targets`. This avoids rewriting the oversized shared project file and inserts the enterprise endpoint map exactly once.

The existing Analytics Center build-time injector remains the sole owner of the generated Module 030 mount and registry identity. Canonical `App.jsx`, `main.jsx`, More-menu security, and the module registry are not committed by this package.

## Release reconciliation

The package was revalidated after the Celar AI regression-gate correction was merged to `main` at `592d4dfcab40131dd0a4a318f28b24dd3de7063a`. This documentation-only checkpoint triggers a fresh pull-request merge candidate and exact-head validation without changing Analytics behavior or migration 060.

## No deployment

This source package does not:

- merge itself;
- apply migration 060;
- send a Test or Production email during development;
- deploy an API or web image;
- change Azure, Container Apps, Entra, Key Vault, DNS, or networking;
- change Module 065 credentials or provider state; or
- expand user record or financial-field authority.
