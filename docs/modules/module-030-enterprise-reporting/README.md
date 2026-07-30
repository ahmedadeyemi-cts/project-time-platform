# Module 030 — Enterprise Reporting Center

## Purpose

Module 030 is the enterprise reporting system for ProjectPulse. It replaces the narrow financial-report presentation with one dynamic, role-scoped report catalog across project delivery, customers, financials, time, utilization, engineers, Project Managers, sales, billing, closeout, notifications, operational controls, security-adjacent evidence, governance, and customer acceptance.

The implementation contains **24 report types** and is designed so additional governed source tables introduced by Groups 8–11 become reportable without rebuilding the page.

## Report-specific filters

The user chooses a report before configuring filters. Each report publishes only the filters that apply to that report, such as:

- customer;
- project;
- Project Manager;
- engineer;
- project status;
- budget status;
- contract type;
- billable status;
- date range;
- workflow status;
- severity;
- module;
- source status;
- free-text search; and
- maximum rows.

Filter options are generated from server-authorized records. The browser never receives an organization-wide person, project, customer, or financial option merely because a user can open Module 030.

## Role and record scope

### Engineer

An Engineer can run reports only for:

- the Engineer's own time and utilization;
- projects assigned to the Engineer;
- project teams and delivery records visible through those assignments; and
- financial fields already made visible by the authoritative project-financial model.

The `engineerUserId` filter is locked to the effective user for an Engineer-only session.

### Project Manager

A Project Manager can run reports only for:

- projects managed by the effective Project Manager;
- engineers assigned to those projects;
- time, delivery, budget, billing, closeout, and customer records associated with those projects; and
- role-appropriate financial fields.

The `projectManagerUserId` filter is locked to the effective user for a non-broad Project Manager session.

### Broader roles

Accounting, Project Team Coordinator, executive, platform, and administrative roles can receive broader choices only where their existing server roles and permissions provide that scope. Report permissions do not create project, customer, person, or field access.

View-As remains read-only. A preview may use the effective user scope, but report-run persistence, saved-view changes, and export audit writes require the actual user outside View-As.

## Report catalog

The initial catalog includes:

1. Project Portfolio
2. Project Financial Health
3. Budget, Forecast & Variance
4. Project Hours Consumption
5. Time Entry Detail
6. Engineer Workload & Assignment
7. Engineer Utilization
8. Project Manager Portfolio
9. Project Team Assignments
10. Customer Project Summary
11. Project Expense Detail
12. SELL & Delivery Context
13. Billing Readiness
14. Project Closeout Readiness
15. Notification Delivery
16. Qualifications & Certification Expiration
17. On-Call Coverage
18. Issues, Defects & Feature Requests
19. Release & Deployment Readiness
20. Service Health, SLO & Error Budget
21. Data Governance & Retention
22. Customer Delivery & Acceptance
23. Secure Project Information
24. Enterprise PMO Project Controls

Reports 18–24 automatically begin returning rows as their separately governed source migrations are installed. Before then, the source is identified precisely as unavailable rather than represented as an empty production inventory.

## Enterprise reporting actions

Authorized users can:

- search the report catalog;
- select a category;
- load report-specific filter choices;
- preview without persistence;
- run and create an immutable execution record;
- save personal report views;
- review run history;
- export XLSX, CSV, or JSON; and
- inspect independent source status and diagnostic codes.

Every XLSX, CSV, or JSON export creates immutable evidence containing the run, actual actor, format, row count, timestamp, and SHA-256 checksum.

## Source isolation

Every report source publishes:

- source key;
- friendly name;
- required or optional status;
- health state;
- role-scoped record count;
- friendly message;
- sanitized diagnostic code; and
- observation timestamp.

One unavailable source does not blank rows returned by healthy sources. Report states are:

- `complete`;
- `partial`;
- `no_data`;
- `source_unavailable`; and
- `failed` for persisted evidence if a future execution stage records it.

## Migration 054

`054_enterprise_reporting_center.sql` creates:

- `enterprise_report_runs` — immutable report execution evidence;
- `enterprise_report_saved_views` — editable personal views with versioning; and
- `enterprise_report_exports` — immutable export evidence.

It adds:

- `VIEW_ENTERPRISE_REPORTING`;
- `RUN_ENTERPRISE_REPORTING`;
- `EXPORT_ENTERPRISE_REPORTING`; and
- `MANAGE_ENTERPRISE_REPORTING`.

The migration grants run/export access to delivery roles, but all records and sensitive fields remain constrained by the existing authoritative sources.

## Shared-file overlap

The source package modifies two established shared integration points:

- `src/backend/ProjectTime.Api/ProjectTime.Api.csproj` to register `MapEnterpriseReportingEndpoints()` in the generated Program before `app.Run()`; and
- `src/frontend/project-time-web/package.json` to run the Module 030 injector and validator after Groups 5–7.

Canonical `App.jsx` and `module-availability-registry.js` are not committed. The idempotent injector replaces only the generated Group 5 Module 030 mount, preserves Module 031 Financial Operations Workbench, and updates the generated Module 030 identity to **Enterprise Reporting Center**.

## Validation

Validation covers:

- migration apply, idempotence, permissions, immutable evidence, rollback, and reapply;
- at least 24 report definitions;
- report-specific filters;
- Engineer self-scope and Project Manager own-portfolio locks;
- source-isolated loading;
- actual/effective-session evidence;
- preview, run, history, saved views, and XLSX/CSV/JSON exports;
- API Release build;
- complete frontend production build;
- Group 3 financial-truth compatibility;
- Groups 5–7 regression validation; and
- full ProjectPulse CI.

## Explicit exclusions

- No deployment.
- No migration execution against Test or Production.
- No Azure or Container Apps change.
- No provider credential change.
- No direct expansion of project, customer, person, or financial-field access.
- No More-menu security change.
