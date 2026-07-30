# Module 030 — Analytics Center

## Purpose

Module 030 is the ProjectPulse **Analytics Center**. It replaces the former Financial Report Center, Enterprise Reporting Center label, and the legacy Reporting / Accounting / Invoicing / Analytics command page with one intuitive, role-scoped analytics experience.

The page follows a simple workflow:

1. Select a report.
2. Configure only the criteria that apply to that report.
3. Preview the result or run and save immutable evidence.
4. Export a saved run to XLSX, CSV, or JSON.
5. Review run history and independent source status.

The initial catalog contains **24 report types** across project delivery, customers, financials, time, utilization, Engineers, Project Managers, teams, sales, billing, closeout, notifications, qualifications, on-call coverage, issue and feature management, release controls, service health, governance, customer acceptance, secure project information, and PMO controls.

## User-interface corrections

The Analytics Center does not expose the former technical closeout checklist or the legacy 030A–030Q command-center structure. It removes:

- Fiscal Period, because Start Date and End Date provide the required reporting range;
- Organization, because Customer is the governed business dimension;
- the 030Q Reporting Readiness Closeout checklist;
- Build Export Layout;
- Save Report Definition Preview;
- browser-only readiness validation controls; and
- the obsolete `/api/reports/030/filter-options` request path.

The page uses the official Group 6 US Signal enterprise presentation components and contains no independent page scroll trap. Only naturally wide result tables scroll horizontally.

## Report-specific criteria

The selected report publishes its own criteria. The page does not display one large unrelated filter form for every report.

Available criteria include, only where applicable:

- Start Date and End Date;
- Customer;
- Project;
- Engineer;
- Project Manager;
- Team;
- Project Status;
- Budget Status;
- Contract Type;
- Billable status;
- Workflow status;
- Severity;
- Module;
- source status;
- free-text search; and
- maximum result rows.

Customer, Project, Engineer, Project Manager, and Team choices are populated by the API from current role-authorized ProjectPulse data. Customer choices originate from the Customer Directory `clients` table. Team choices originate from active `teams` and current `team_memberships`. Project and people choices are limited to the effective user's authorized project portfolio.

Selecting a Customer, Project, or Team refreshes dependent criteria so unrelated Project, Engineer, and Project Manager choices are removed.

## Contract types

Analytics uses the same canonical contract types as Modules 055C and 055D:

- Fixed Price;
- Time and Material;
- Pre-Sales;
- Internal;
- Non-billable; and
- Other.

Legacy aliases such as `T&M`, `TM`, `FP`, `Fixed Fee`, and `Pre-Sales` are normalized to the same reporting values before filtering.

## Role and record scope

### Engineer

An Engineer can analyze only:

- the Engineer's own time and utilization;
- projects and tasks assigned to the Engineer;
- delivery records visible through those assignments; and
- financial fields already authorized by the project-financial model.

The `engineerUserId` criterion is locked to the effective user for an Engineer-only session. A browser-provided Engineer identifier never expands scope.

### Project Manager

A Project Manager can analyze only:

- projects managed by the effective Project Manager;
- customers connected to those projects;
- Engineers and teams assigned to those projects;
- related time, delivery, budget, billing, closeout, and notification evidence; and
- role-appropriate financial fields.

The `projectManagerUserId` criterion is locked to the effective user for a non-broad Project Manager session.

### Broader roles

Accounting, Project Team Coordinator, executive, management, sales, platform, and administrative roles receive broader choices only where their existing server roles and permissions grant the underlying records and fields. Analytics authority does not create customer, project, person, or financial-field access.

View-As remains preview-only. Persisting a run and creating export evidence require the actual user outside View-As.

## Report catalog

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

Reports backed by later Group 8–11 tables identify the exact unavailable source until those separately governed migrations exist. They never display a fabricated empty production inventory.

## Working actions

The Analytics Center buttons call registered APIs:

- Refresh Analytics → catalog, history, and active filter refresh;
- Reset criteria → restores the selected report's defaults;
- Refresh filter lists → reloads current server-authorized choices;
- Preview report → `/api/analytics/preview`;
- Run & save → `/api/analytics/run`;
- Export XLSX/CSV/JSON → immutable saved-run export; and
- Refresh history → `/api/analytics/history`.

The compatibility `/api/enterprise-reporting` API remains registered for internal migration safety, but the Module 030 page uses `/api/analytics`.

## Source isolation

Every report source publishes its key, friendly name, required/optional state, health, role-scoped record count, friendly message, sanitized diagnostic code, and observation timestamp.

One unavailable optional source does not blank rows returned by healthy sources. Result states are:

- `complete`;
- `partial`;
- `no_data`;
- `source_unavailable`; and
- `failed` for persisted execution evidence when applicable.

## Migration 054 and immutable evidence

`054_enterprise_reporting_center.sql` remains the compatibility-stable migration identifier. It creates:

- `enterprise_report_runs` — immutable analytics execution evidence;
- `enterprise_report_saved_views` — editable personal definitions with versioning; and
- `enterprise_report_exports` — immutable export evidence with SHA-256 checksum.

Existing permission codes remain compatibility-stable:

- `VIEW_ENTERPRISE_REPORTING`;
- `RUN_ENTERPRISE_REPORTING`;
- `EXPORT_ENTERPRISE_REPORTING`; and
- `MANAGE_ENTERPRISE_REPORTING`.

## Shared-file overlap

The package changes only established additive integration points:

- `ProjectTime.Api.csproj` registers `MapEnterpriseReportingEndpoints()` for compatibility and `MapAnalyticsCenterEndpoints()` for the current page;
- frontend `package.json` runs the Module 030 injector and validator after Groups 5–7.

Canonical `App.jsx` and `module-availability-registry.js` are not committed. The idempotent injector replaces the generated Module 030 reporting mount with `AnalyticsCenter`, preserves Module 031, and changes the generated Module 030 identity to **Analytics Center**.

## Validation

Validation covers:

- migration 054 apply, idempotence, permissions, immutable evidence, rollback, and reapply;
- at least 24 report definitions;
- report-specific criteria;
- Customer Directory, Project, Engineer, Project Manager, and Team option sources;
- Modules 055C and 055D contract-type alignment;
- removal of Fiscal Period, Organization, and 030Q;
- Engineer self-scope and Project Manager own-portfolio locks;
- source-isolated loading;
- preview, run, immutable history, and XLSX/CSV/JSON export;
- API Release build;
- complete frontend production build;
- Groups 3 and 5–7 compatibility; and
- full ProjectPulse CI.

## Explicit exclusions

- No deployment.
- No migration execution against Test or Production.
- No Azure or Container Apps change.
- No provider credential change.
- No direct expansion of customer, project, person, or financial-field access.
- No More-menu security change.
