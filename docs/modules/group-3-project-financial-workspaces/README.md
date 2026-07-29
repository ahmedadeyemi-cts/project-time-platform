# Group 3 — Unified Project Financial Truth and Team Workspaces

## Scope

This package unifies Modules 018, 019, 036, and 055B around one read-only authoritative project financial API. It reconciles project ownership, assignments, approved and in-flight time, current Module 005 expenses, Module 019 documents, Module 022 cost-alert evidence, governed rate cards, and the Module 026 SELL commercial read model.

The package is source-only. No database migration, deployment workflow, Azure operation, provider connection, credential change, external SELL request, or test/production resource mutation is included.

## Existing responsibilities inspected

| Module | Existing responsibility | Gap addressed by Group 3 |
|---|---|---|
| Module 018 | Project Manager workload, project status, risk highlights, assigned resources, tasks, planned hours, and planned cost | No single source for actual cost, current expenses, forecast, variance, budget state, notification evidence, or calculation explanations |
| Module 019 | Role-scoped project workspace, assignments, engineering-visible documents, resource requests, and working downloads | Financial and progress data were separate from document and assignment context; the unified experience adds allocated, used, and remaining hours without depending on Module 011 |
| Module 036 | Sales handoff, intake readiness, PM assignment, source documents, customer signals, and delivery blockers | No authoritative SELL association, project financial status, budget warnings, forecast, variance, or assigned delivery-team summary |
| Module 055B | Governed rate-card administration | Project/customer financial context was separate from the governed SELL commercial model and could encourage duplicate connection logic |

## Authoritative project summary

The API provides the following shared project-level fields:

- customer and project identity;
- Project Manager;
- assigned engineers;
- Solution Architect;
- Account Executive;
- Project Team Coordinator;
- contract type;
- contracted value when recorded;
- labor budget;
- expense budget;
- planned hours;
- used hours;
- remaining hours;
- calculated labor cost;
- current uploaded expenses;
- committed cost;
- forecasted final cost;
- current variance;
- completion percentage;
- budget status;
- notification and cost-alert evidence; and
- governed SELL association.

Unknown or restricted values remain `null`, `not_recorded`, or explicitly restricted. The API does not convert missing data into zero and does not fabricate a contract value, budget, rate, cost, or SELL relationship.

## Source authority

### Projects and customer ownership

The `projects` and `clients` tables remain authoritative for project identity, customer, current status, dates, project ownership, contract type, and provider references already stored with the project.

### Assignments and planned hours

`project_assignments` and `engineering_resource_request_assignments` supply assigned engineers and planned hours. A direct assignment-hour value takes priority; governed resource-request allocation is used only when a direct assignment-hour value is not present.

### Used hours

`time_entries` supplies used project hours. Voided, rejected, or declined entries are excluded. The response labels this as ProjectPulse project time and does not imply external payroll or accounting settlement.

### Module 005 expenses

Only current, non-deleted Module 005 uploads are included:

```text
project_expense_uploads.is_current = TRUE
project_expense_uploads.deleted_at IS NULL
```

Superseded and deleted upload evidence remains excluded from current project totals. Existing pass-through and fixed-price expense treatment remains visible.

### Module 019 documents

The shared response groups role-visible documents as:

- IQS files;
- service requests;
- project documents; and
- customer documents.

Downloads use the existing Module 019 role-scoped endpoint. No Module 011 dependency is created.

### Module 022 evidence

Existing open cost-alert and queued-notification evidence is consumed read-only. Group 3 does not create configurable alert-routing or notification schedules. Those responsibilities remain assigned to Group 4.

### Module 026 SELL ownership

PR #187 completed the Module 021/026 SELL connection foundation and migration 049. Group 3 calls `SellCommercialReadModelModule.LoadProjectCommercialSummaryAsync` and therefore consumes the governed Module 026 connection, readiness, quote association, rate card, rate lines, and synchronization status.

Module 055B does not create or maintain a second SELL credential, secret, connector, health registry, connection-test path, or synchronization system.

## Calculations

### Remaining hours

```text
max(planned hours - used hours, 0)
```

### Calculated labor cost

```text
used hours × effective governed hourly rate
```

The preferred rate basis is the governed Module 026 SELL/current commercial rate model. When no governed rate line is available but a labor budget and planned hours are known, the API can expose a clearly labeled budget-derived rate estimate. This is a project-cost estimate and is never represented as payroll cost.

### Committed cost

```text
calculated labor cost + current non-deleted Module 005 expenses
```

### Forecasted final cost

```text
(used hours + remaining hours) × effective rate + current expenses
```

### Current variance

```text
known project budget - forecasted final cost
```

The response identifies whether the budget basis contains both labor and expense budgets or only the labor budget. Missing expense-budget information is never silently treated as a complete financial plan.

### Completion percentage

```text
used hours ÷ planned hours × 100
```

A value above 100 percent indicates that used hours exceeded the current assignment plan.

## Role-aware workspaces

### Module 018 — Project Manager workspace

Module 018 receives:

- a searchable project portfolio;
- project expense detail;
- labor and expense budget visibility;
- actual and committed project-cost estimates;
- forecast and variance;
- approaching-budget and over-budget status;
- cost-alert and notification status;
- project drill-down; and
- calculation explanations.

Project Managers see their managed projects. PM team leads can select only Project Managers within their governed team scope. Project Team Coordinators, authorized financial roles, executives, and administrators retain broader server-enforced scope.

### Module 019 — Engineering workspace

Module 019 receives:

- allocated hours;
- used hours;
- hours remaining;
- project progress;
- cost visibility appropriate to the current role;
- documents grouped by type;
- authenticated working downloads;
- IQS files;
- service requests;
- project documents; and
- customer documents.

Engineering users receive hours and progress by default. Detailed labor-cost, budget, forecast, and variance amounts remain restricted unless another governed role or project ownership grants them.

### Module 036 — Sales workspace

Module 036 receives:

- sales-owned projects;
- SELL quote and readiness association;
- customer and opportunity context already stored with the project;
- project financial status;
- delivery risk;
- approaching-budget and over-budget warnings; and
- assigned Project Manager, engineers, Solution Architect, Account Executive, and Project Team Coordinator.

Sales receives commercial summary visibility without detailed labor-cost basis unless another role grants full project financial access.

### Module 055B — Rate Card Administration context

Module 055B retains all existing rate-card administration controls. The new context panel shows which visible projects and customers consume governed rate information, their Module 026 SELL association, commercial readiness, and current project financial state.

## Source-level resilience

The project source is required. Optional assignment, time, expense, alert, document, metadata, and SELL sources are loaded independently. A failed optional source is returned with:

- source name;
- required/optional classification;
- friendly status;
- sanitized diagnostic code;
- returned record count; and
- a source-level retry path.

One unavailable optional source does not blank the complete page. Raw database messages, connection strings, provider credentials, and secret values are not returned.

## API contract

The package adds four authenticated GET endpoints:

- `GET /api/project-financials/portfolio`
- `GET /api/project-financials/projects/{projectId}`
- `GET /api/project-financials/sources`
- `GET /api/project-financials/reporting-summary`

Supported workspace values are `pm`, `engineering`, `sales`, and `rate-card`. Search, budget/project status, Project Manager scope, and bounded result filters are read-only query parameters.

## Web integration and branding

`UnifiedProjectFinancialWorkspace.jsx` is installed additively into the existing Module 018, 019, 036, and 055B component roots through an idempotent predevelopment/prebuild installer. It does not rewrite `App.jsx`, `main.jsx`, navigation, or the module registry.

The experience uses the approved `usSignalLogoDataUrl` image asset and centralized enterprise navy, cyan, green, warning, risk, and neutral design tokens. Existing module-specific content remains below the unified panel.

Group 6 will later standardize reusable branding and page-structure primitives across the wider module set. Group 3 does not preempt that source issue by rewriting unrelated pages.

## Shared-file overlap

Two additive shared integration points are required:

1. `ProjectTime.Api.csproj` registers `app.MapProjectFinancialTruthEndpoints();` through the existing generated Program sequence.
2. Frontend `package.json` runs the idempotent Group 3 installer and source validator in the existing development and production build chains.

Open PR #218 also touches these two files for Group 2B. Group 3 remains based on the current protected main and must be reconciled normally if main advances; no force-push or overwrite of newer work is authorized.

## Migration and deployment declaration

No migration is included. No migration number was selected or reserved.

No deployment workflow is created or modified. No deployment, Azure operation, Container Apps update, database write, provider call, credential change, email delivery, alert creation, notification schedule, or production/test resource mutation is part of this source PR.

## Separate future groups

Groups 4, 5, and 6 remain separate source issues and PRs:

- Group 4 owns configurable cost-alert routing and notification scheduling through Module 065 mail.
- Group 5 owns financial reporting, Certify, closeout, invoice, billing, and source-specific recovery work.
- Group 6 owns reusable page structure and official US Signal branding across the wider module set.
