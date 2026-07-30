# Module 030 — Analytics Center

## Purpose

Module 030 is the **Analytics Center**, an intuitive reporting platform for technical and nontechnical ProjectPulse users. It replaces the legacy Reporting/Accounting/Invoicing command center and the narrower Financial Report Center presentation.

The legacy Test interface repeatedly failed while rendering Engineer criteria because `selectedEngineerSummaryText` was undefined. That exception interrupted filter loading, preview generation, CSV export, export-layout generation, report-definition saving, readiness validation, and subsequent rerenders. The Analytics Center does not use that legacy browser implementation or `/api/reports/030/filter-options`.

## User workflow

The page follows one consistent workflow:

1. **Select report** — choose from a searchable, categorized catalog.
2. **Set criteria** — show only filters that apply to the selected report.
3. **Preview report** — calculate current role-scoped results without persistence.
4. **Run & save** — record immutable run evidence.
5. **Export** — create XLSX, CSV, or JSON and record immutable checksum evidence.
6. **Run history** — review and re-export authorized prior runs.

Every action provides visible loading, success, no-data, partial-source, authorization, or failure feedback.

## Report coverage

The catalog contains 24 report types covering:

- projects and customer portfolios;
- project financial health, budgets, forecast, cost, and variance;
- project hours and time-entry detail;
- Engineer workload and utilization;
- Project Manager portfolios;
- project-team assignments;
- customer project summaries;
- current Module 005 expenses;
- Module 026 SELL and delivery context;
- billing and closeout readiness;
- notification delivery;
- qualifications and certification expiration;
- on-call coverage;
- issue and feature-request lifecycle;
- release and deployment readiness;
- service health and SLOs;
- data governance and retention;
- customer delivery acceptance;
- secure project information; and
- PMO project controls.

## Dynamic criteria

The selected report definition determines which filters appear. Supported criteria include:

- Customer;
- Project;
- Engineer;
- Project Manager;
- Team;
- Start Date and End Date;
- project status;
- budget status;
- contract type;
- billable state;
- workflow status;
- severity;
- module;
- source status;
- search; and
- maximum rows.

The Analytics Center does not show Fiscal Period, Organization, Cadence, the 030Q readiness checklist, Build Export Layout, Save Report Definition Preview, or Validate 030 Readiness.

## Populated filter sources

| Criterion | Default | Source |
|---|---|---|
| Customer | All customers | Current role-scoped Customer Directory records |
| Project | All projects | Current role-scoped projects |
| Engineer | All engineers | Engineers assigned to visible projects |
| Project Manager | All Project Managers | PMs associated with visible projects |
| Team | All teams | Current active team memberships intersecting visible project users |
| Contract Type | All contract types | Modules 055C/055D canonical contract values |

Customer, Project, and Team selections trigger a cascading filter refresh. Filter options are calculated on the server and never expand record scope.

## Contract-type alignment

Canonical values match Modules 055C and 055D:

- Fixed Price
- Time and Material
- Pre-Sales
- Internal
- Non-billable
- Other

Aliases such as T&M, TM, Fixed Fee, and FP are normalized before filtering.

## Role scope

### Engineers

Engineer-only sessions are locked to the effective Engineer for person-level time, workload, utilization, assignments, tasks, and related projects. An Engineer cannot choose another user identifier to expand scope.

### Project Managers

Project Manager sessions are locked to projects managed by the effective PM or otherwise authorized through the existing PM scope. A PM cannot choose an unrelated PM, customer, or project to bypass server scope.

### Broader roles

Accounting, Billing, Finance, PTC, Executive, Manager, Sales, Solution Architect, Administrator, and Super Administrator access remains governed by existing record and financial-field permissions. Analytics permission does not grant new customer, project, employee, or sensitive financial access.

View-As remains preview-only. Run persistence, saved-view mutation, and export are blocked while View-As is active.

## Source isolation

Every report identifies its required and optional sources. One unavailable optional source produces a partial result rather than blanking healthy rows. Responses distinguish:

- complete;
- partial;
- no_data;
- source_unavailable; and
- failed.

User-facing messages remain friendly. Sanitized diagnostic codes, source names, timestamps, and correlation evidence remain available for support.

## API

```text
GET    /api/analytics/catalog
POST   /api/analytics/filter-options
POST   /api/analytics/preview
POST   /api/analytics/run
GET    /api/analytics/history
GET    /api/analytics/runs/{runId}/export?format=xlsx|csv|json
```

The internal `/api/enterprise-reporting` route remains temporarily registered as an export and saved-view compatibility surface. It is not the user-facing Module 030 identity.

## Persistence and immutable tracking

Migration `055_analytics_center` creates:

- `enterprise_report_runs` — immutable role/scope/filter/source/result snapshots;
- `enterprise_report_saved_views` — personal versioned saved views; and
- `enterprise_report_exports` — immutable export format, row count, and SHA-256 evidence.

Run and export evidence cannot be updated or deleted through normal SQL operations. Saved views remain editable and versioned.

Migration 055 was selected after inspecting current `main` and open PRs. PR #323 owns a separate migration 054, so this package does not reuse migration number 054.

## Enterprise presentation

The page consumes the Group 6 enterprise presentation system and approved US Signal image asset. It provides:

- one responsive page shell;
- a searchable report picker;
- report-specific criteria;
- accessible controls and focus states;
- clear empty and warning states;
- constrained horizontal result-table overflow;
- no competing nested page scroll areas; and
- print/export behavior that hides builder controls.

## Shared-file overlap

Two shared integration points are required:

- `ProjectTime.Api.csproj` registers the Analytics and compatibility endpoints and generates migration-055 compile copies from the earlier abandoned migration-054 source package.
- frontend `package.json` adds the idempotent Analytics installer and validator after Groups 5–7.

Canonical `App.jsx`, `main.jsx`, and `module-availability-registry.js` are not committed. The build-time installer replaces the legacy Module 030 mount and identity exactly once.

## Explicit exclusions

- No deployment.
- No connected migration execution.
- No Azure or Container Apps operation.
- No More-menu security change.
- No provider credential change.
- No expansion of record or financial-field access.
