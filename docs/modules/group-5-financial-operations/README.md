# Group 5 — Financial Reports, Closeout, Source Recovery, and Billing

## Purpose

Group 5 restores usable financial reporting, billing-readiness, closeout, notification-recovery, and invoice/billing workspaces without allowing one unavailable source to blank an otherwise complete page.

The package owns:

- **Module 030** — Financial Report Center
- **Module 031** — Financial Operations Workbench
- **Module 039** — billing-readiness and reconciliation recovery
- **Module 040** — project closeout recovery
- **Module 041** — closeout data, access attribution, source recovery, and workspace continuity
- **Module 042** — approved-time and current-expense billing recovery

**Module 038 is regression-only.** This package does not change Certify layout, scrolling, controls, connection behavior, manual synchronization, automatic synchronization, or safeguards.

Group 4 continues to own Module 041 notification routing. Module 065 continues to own provider configuration, credentials, sender identity, delivery boundary, and external delivery.

## Dependency baseline

Group 5 is stacked on the final Group 4 branch because Module 041 recovery reads Group 4 dispatch evidence. It also consumes current-main Group 3 project-financial truth instead of creating another calculation system.

The Group 3 source remains authoritative for:

- projects and customers;
- project scope;
- Project Manager and assigned team;
- planned and used hours;
- labor and expense budgets;
- current Module 005 expenses;
- governed rate context;
- labor cost;
- committed cost;
- forecasted final cost;
- current variance;
- completion percentage;
- cost-alert state; and
- Module 026 SELL context.

`ProjectFinancialTruthReportingBridge.cs` extends the current Group 3 class through a generated partial declaration. The reviewed Group 3 source is not rewritten, and Group 5 does not maintain competing project-financial SQL.

## Migration 051

`051_financial_operations_reporting_recovery.sql` creates:

1. `financial_report_runs`
2. `financial_operations_work_items`
3. `financial_operations_actions`

`financial_operations_actions` is immutable audit evidence. The migration also adds the Module 030, Module 031, and Module 039–042 recovery permissions and role grants.

Migration 051 is included in source but has not been applied to Azure, test, production, or any connected ProjectPulse database.

## Module 030 — Financial Report Center

Module 030 contains a functioning report catalog rather than placeholder tiles.

### Report catalog

The initial catalog includes:

1. **Project Financial Health**
   - budgets;
   - labor cost;
   - current expenses;
   - committed cost;
   - forecast;
   - variance;
   - completion;
   - budget status; and
   - SELL readiness.

2. **Project Hours Consumption**
   - planned hours;
   - used hours;
   - approved invoice-eligible hours;
   - remaining hours;
   - completion percentage; and
   - hours status.

3. **Project Expense Status**
   - current non-deleted Module 005 uploads;
   - owner;
   - period;
   - source mode;
   - amount;
   - reimbursable amount; and
   - billing treatment.

4. **Billing Readiness**
   - approved time;
   - approved labor estimate when a governed cost-rate basis is available;
   - current expenses;
   - latest billing-readiness review;
   - forecast;
   - variance;
   - SELL readiness; and
   - blockers.

5. **Project Closeout Readiness**
   - project state;
   - governed closeout state;
   - billing disposition;
   - approved time;
   - billing readiness;
   - open cost alerts;
   - Group 4 notification state; and
   - closeout blockers.

6. **Notification Delivery**
   - Group 4 dispatch;
   - source module;
   - severity;
   - server-derived recipient count;
   - Module 065 delivery boundary;
   - delivery state;
   - diagnostic code; and
   - sent timestamp.

### Report actions

Authorized users can:

- search reports;
- filter by project, customer, status, and date;
- preview results without persistence;
- run and persist a report;
- review run history;
- export the persisted result as CSV; and
- distinguish `complete`, `partial`, `no_data`, and `source_unavailable` states.

A report with healthy core data and one failed optional source remains visible as `partial`.

## Module 031 — Financial Operations Workbench

Module 031 is the productivity enhancement selected from the available Modules 031–035.

It is one accountable queue for:

- unavailable financial sources;
- missing project-financial information;
- approaching-budget and over-budget projects;
- missing or incomplete billing packages;
- completed projects without governed closeout;
- failed or held closeout/cost notifications;
- approved-time and expense completeness conflicts;
- owner;
- priority;
- first and last detection times;
- source-level Retry;
- acknowledgement;
- resolution; and
- immutable action history.

The refresh operation derives current queue items from the same role-scoped data returned to the module pages. Resolved items reopen only when the underlying condition is detected again.

## Source isolation and friendly errors

Each source returns:

- source key;
- display name;
- required or optional classification;
- `healthy`, `partial`, or `unavailable` status;
- record count;
- observation time;
- sanitized diagnostic code;
- friendly message; and
- source-specific Retry endpoint.

Sources include:

- projects;
- assignments;
- time entries;
- approved time entries;
- current Module 005 expenses;
- billing-readiness reviews;
- project closeout records;
- Group 4 notification dispatches;
- project metadata;
- cost alerts; and
- Module 026 SELL commercial model.

Raw database exceptions, connection strings, credentials, request bodies, and provider secrets are not returned to the browser. Correlation IDs and sanitized diagnostic codes remain available for technical support.

## Actual-session and effective-session attribution

Every Group 5 response identifies:

- actual user;
- effective user;
- View-As state;
- read-only View-As boundary;
- server-verified project scope; and
- the fact that View-As never transfers mutation authority.

Report persistence, workbench refresh, retry, acknowledgement, and resolution require the actual user and are blocked during View-As.

## Module 039 — Billing-readiness recovery

Module 039 gains a recovery panel above its existing operational interface. It shows:

- approved time;
- current expenses;
- latest billing package review;
- package type and period;
- forecast and variance;
- exact unavailable source;
- source-level Retry; and
- financial blockers.

The existing Billing Readiness Center remains intact below the recovery panel.

## Module 040 — Project closeout recovery

Module 040 gains:

- governed closeout state;
- billing disposition;
- approved-time evidence;
- billing package readiness;
- open cost-alert count;
- closeout notification state;
- exact source failures; and
- source-specific Retry.

Healthy project and billing content remains visible when closeout history or notification evidence is unavailable.

## Module 041 — Closeout notification recovery

Group 5 owns Module 041 closeout data, access attribution, source recovery, and workspace continuity.

The recovery panel reads:

- project closeout state;
- Group 4 dispatch status;
- server-derived recipient count;
- Module 065 delivery boundary;
- failure code and message; and
- source-specific Retry.

Group 5 does not send mail. The page explicitly identifies **Group 4 routing and Module 065 delivery** as the notification authority.

## Module 042 — Invoice and billing recovery

Module 042 is not replaced with another Module 005 page.

The recovery panel contains only an intentional expense summary and drill-down:

- current upload count;
- current expense total;
- reimbursable total;
- latest current uploads;
- owner;
- period;
- source;
- billing treatment; and
- upload timestamp.

It combines that summary with approved time, billing-readiness review, forecast, variance, and source evidence. The existing invoice and billing interface remains below it.

The panel uses the approved US Signal image asset and contains no generated or substitute branding.

## Module 038 — regression-only boundary

The Group 5 injector, API, migration, and UI do not target Module 038. The focused workflow rejects any changed Certify component or Module 038 backend source and runs existing Module 005/038 validators as regression checks.

## Permissions

Migration 051 adds:

- `VIEW_FINANCIAL_REPORT_CENTER`
- `RUN_FINANCIAL_REPORTS`
- `EXPORT_FINANCIAL_REPORTS`
- `VIEW_FINANCIAL_OPERATIONS_WORKBENCH`
- `MANAGE_FINANCIAL_OPERATIONS_RECOVERY`
- `RETRY_FINANCIAL_SOURCES`
- `VIEW_ACCOUNTING_RECONCILIATION_RECOVERY`
- `VIEW_PROJECT_CLOSEOUT_RECOVERY`
- `VIEW_CLOSEOUT_NOTIFICATION_RECOVERY`
- `VIEW_BILLING_RECOVERY`

### Role intent

| Team | Access |
|---|---|
| Accounting, Billing, Finance | Full report, export, workbench, retry, closeout, reconciliation, and billing recovery |
| Project Team Coordinator | Full operational recovery authority |
| Super Administrator, Administrator | Full authority |
| Project Management | Scoped reports, Module 031 visibility, closeout, notification, and billing recovery |
| Executive | Read-only organization reports and recovery visibility |
| Engineering, Manager, Sales, Inside Sales, Solution Architect | Role-scoped report viewing and running through Group 3 visibility rules |

Detailed labor-cost visibility remains role-appropriate. A report permission does not expand underlying project or financial scope.

## APIs

- `GET /api/financial-operations/reports/catalog`
- `POST /api/financial-operations/reports/preview`
- `POST /api/financial-operations/reports/run`
- `GET /api/financial-operations/reports/history`
- `GET /api/financial-operations/reports/runs/{runId}/export`
- `GET /api/financial-operations/sources`
- `POST /api/financial-operations/sources/{sourceKey}/retry`
- `GET /api/financial-operations/workbench`
- `POST /api/financial-operations/workbench/refresh`
- `POST /api/financial-operations/workbench/{workItemId}/{action}`
- `GET /api/financial-operations/modules/{moduleCode}`

## Shared-file overlap

Group 5 adds one endpoint registration and a generated partial copy of Group 3 through `ProjectTime.Api.csproj`. Frontend `package.json` adds the Group 5 injector and validator after Group 4.

Canonical `App.jsx` and `module-availability-registry.js` are not committed. The idempotent injector adds Modules 030 and 031 plus recovery panels for Modules 039–042 during predevelopment and prebuild.

## Validation

The focused workflow must pass:

- exact stacked-source scope;
- migration 051 apply, idempotence, role grants, immutability, rollback, and reapply;
- Group 5 source contract;
- API Release build;
- Group 3 and Group 4 compatibility;
- Module 005 and Module 038 regression validation;
- Work-to-Cash lifecycle validation;
- complete frontend production build;
- web-container context; and
- full ProjectPulse CI.

## Explicit exclusions

- No deployment.
- No migration execution against a connected environment.
- No Module 038 layout or behavior change.
- No alternate mail implementation.
- No Module 067 configuration read.
- No Module 005 duplicate page.
- No Azure or Container Apps operation.
- No FlowHive implementation.
