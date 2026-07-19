# ProjectPulse Module Catalog

## Catalog rules

- The catalog records what current source contains and what work is approved.
- `Installed` means the route or global behavior exists on current `main`; it does
  not independently assert deployment or business acceptance.
- `Source candidate` means committed, pushed, and reviewed through an open PR; it
  does not mean merged, registered, deployed, or runtime-active.
- `Runtime-active` requires shared registration, merge, deployment, and successful
  portal verification.
- Proposed retirement or reuse never authorizes removal of current behavior.
- A module number cannot be reassigned until route, API, database, audit, history,
  documentation, and dependency impacts are explicitly approved.
- Customer-facing PDF and Excel artifacts use the approved US Signal logo.

## Installed modules on `main@04fcafd4f49840428645e537db7de436e34b1c88`

| Module | Current title | Route/scope | Source state | Governance note |
|---|---|---|---|---|
| 001 | Time Entry | `timesheet` | Installed | Active follow-up work is externally owned; preserve current multiview and save behavior |
| 002 | Approval Inbox | `manager-approval` | Installed | Active semantic integration must protect PM, correction, password-reset, and history workflows |
| 003 | Utilization | `utilization` | Installed | Preserve own, manager, and team-lead views |
| 004 | Holiday Calendar | `holiday-admin` | Installed | No current renumbering decision |
| 005 | Project Allocation and Info | `project-allocation-info` | Installed legacy behavior | Tracker proposes retirement/reservation; do not remove without explicit approval |
| 006 | PSA Modules | `psa-modules` | Installed legacy behavior | Tracker proposes retirement/reuse; preserve until dependency audit is approved |
| 007 | Workflow | `workflow` | Installed, multi-panel | Several workflow functions share this number and route |
| 008 | Audit / Security History | `audit-history` | Installed | Audit authority for future FlowHive history |
| 009 | User Administration | `user-admin` | Installed | Identity and role dependency |
| 010 | Azure / Entra Admin | `azure-admin` | Installed | Azure/Entra changes always require separate authorization |
| 011 | Work Task Builder | `work-task-builder` | Installed | Tracker proposes a future scope decision; current task behavior remains protected |
| 012 | Role Administration | `role-admin` | Installed | Role enforcement dependency |
| 013 | Service Control Center | `service-control` | Installed | Administrative module |
| 014 | Backup / DR Center | `backup-dr` | Installed | Administrative module |
| 015 | Restore Validation | `restore-validation` | Installed | Administrative module |
| 016 | Backup Retention | `backup-retention` | Installed | Administrative module |
| 017 | Replication & Sync Status | `replication-sync` | Installed | Administrative module |
| 018 | Project Workload | `project-workload` | Installed | Project FlowHive portfolio dependency |
| 019 | Project Workspace & Engineering Documents | `project-workspace` | Installed | Canonical role-scoped workspace dependency |
| 020 | Project Intake & Engineering Resource Requests | `project-intake` | Installed | Tracker contains competing future descriptions; current behavior wins until resolved |
| 021 | Customer Directory | `customer-directory` | Installed | Canonical customer dependency |
| 022 | Cost Overrun Alerts | `cost-alerts` | Installed | Future FlowHive risk integration |
| 023 | Time Compliance & Notification Center | `time-compliance` | Installed | Notification and compliance dependency |
| 024 | Sales-to-Delivery Intake Foundation | `sales-intake` | Installed | Restored registry/navigation on current main |
| 025 | SOW Generator + Claude Review Workflow | `sow-generator` | Installed | GSD/SOW generation dependency |
| 026 | CRM Integration Framework | `crm-integration` | Installed | SELL/CRM intake dependency |
| 027 | Signed SOW Handoff + Assignment Trigger | `signed-handoff` | Installed | Assignment and launch dependency |
| 028 | SOW-Aware AI Time Entry Generator | `ai-time-entry` | Installed | AI output remains engineer-reviewed |
| 029 | User Acceptance / Role + Workflow Validation Center | `uat-validation` | Installed | Validation dependency |
| 030 | Reporting / Accounting / Invoicing / Analytics | `reporting` | Installed | Future FlowHive portfolio and export integration |
| 036 | Sales Insights Dashboard | `sales-insights` | Installed | Sales-to-delivery readiness |
| 037 | Roles and Permissions Matrix | `roles-permissions-matrix` | Installed | Least-privilege governance dependency |
| 038 | Certify Integration Center | `certify-integration` | Installed | Expense integration foundation |
| 039 | Billing Readiness Center | `billing-readiness` | Installed | Billing readiness foundation |
| 040 | Project Closeout Center | `project-closeout` | Installed | FlowHive closure dependency |
| 041 | Closeout Email Automation Center | `closeout-email` | Installed | Real email remains governed separately |
| 042 | Invoice & Billing Center | `invoice-billing-center` | Installed route | Open PR 12 is older-lineage work and is not automatically safe to merge |
| 055B | Rate Card Administration | `rate-card-administration` | Installed | Commercial data remains restricted |
| 055C | Work Register | `work-register` | Installed | FlowHive intake, changes, assignments, and closure dependency |
| 056E | Contract-management evolution guard | Cross-cutting source invariant | Protected | Preserve 056E fragments and keep forbidden 056D fragments absent |
| 057 | Resource & Team Calendar Capacity | `calendar-capacity` | Installed | FlowHive calendar and workload dependency |
| 058 | Autonomous CI/CD Foundation | `cicd-pipeline` | Installed | Restored registry/navigation on current main |
| 059 | Global Session Intelligence | All authenticated routes | Installed global behavior | Must remain mounted once on every existing and future module route |
| 060 | Contracts & Block of Hours | `contracts` | Installed | Includes contracts/prepaid management foundations |
| 062 | Unified Identity Profile and Presence | `/api/identity/profile`; Profile, Module 057, and Module 059 consumers | Installed through merged PR 19 | Final head `3852a21e1098de9ad907e3da91e0646d99adcb7c`; merge commit `04fcafd4f49840428645e537db7de436e34b1c88`; post-merge test deployment and portal verification remain pending |
| 063 | Opportunities & Action Tracker | `opportunities` | Installed | Current installed identity owns 063; planned SMTP work must receive another number |
| 999 | ProjectPulse Complete User Guide | `user-guide` | Installed | Global documentation route restored on current main |

## Approved and planned new modules

| Module | Title | Status | Owner/workspace | Dependencies | Number decision |
|---|---|---|---|---|---|
| 061 | Undefined | Scope required | No verified implementation checkpoint | None confirmed | Reserved until explicit scope approval |
| 064 | AI Provider Configuration Center | Planned | Unassigned | Provider governance, secrets, audit | Number available and tracker-assigned |
| 065 | Entra Secret Administration | Planned | Unassigned | Module 010, secure secret controls | Number available and tracker-assigned |
| 066 | Project FlowHive | 066A source candidate — locally validated, committed, pushed, and under PR 20; runtime inactive | `/home/ahmed/project-time-platform-module-066-project-flowhive` | 018, 019, 024–030, 055C, 057, 062, 064 | Number approved and confirmed free before implementation |

## Unresolved numbering and reuse decisions

| Candidate | Conflict | Required decision |
|---|---|---|
| Global SMTP Configuration | Tracker proposes Module 063, but 063 is installed Opportunities | Assign a new unused number; 067 is only a recommendation until approved |
| Module 005 reuse | Current Project Allocation route remains installed; tracker says retire/reserve | Preserve route and history until a formal retirement plan is approved |
| Module 006 reuse | Current PSA Modules route remains installed | Complete route/API/data dependency audit before reuse |
| Module 011 reuse | Current Work Task Builder route remains installed | Do not replace task behavior with qualifications scope without migration approval |
| Module 020 future scope | Tracker names both Integration Status and Work Intake while current source is Project Intake | Reconcile requirements before any rename or replacement |

## Module 066 phase catalog

| Phase | Outcome | Current state |
|---|---|---|
| 066A | Read-only portfolio, task grid, assignment scope, capability/API contract | Foundation commit `ed5ee90e806b9a205225ec4941e558acf6bfb605`; PR 20 open and mergeable; compatible with `main@04fcafd4f49840428645e537db7de436e34b1c88`; shared registration and runtime activation deferred |
| 066A.1 | Shared Registration and Activation | Planning and overlap discovery permitted; implementation must wait for PR 20 and Module 002 to merge; limited to endpoint registration, route/navigation activation, validator/build wiring, and governance updates; no database changes |
| 066B | Database-backed planning persistence: versioned WBS, dependencies, baselines, execution, collaboration, and audit | Separate future phase; explicit database design and database-change authorization required before implementation |
| 066C | Schedule engine, Gantt/timeline/calendar/card views, workload and portfolio risk | Planned |
| 066D | Templates, automations, alerts, API/webhooks, GSD/SOW AI plan generation | Planned; depends on Module 064 |
| 066E | Branded PDF/Excel, customer links, external comment/approval | Blocked until verified US Signal logo and external-sharing controls are approved |

## Current deployment interpretation

| Module | Source status | Runtime-active in portal | Required next step |
|---|---|---|---|
| 062 | Merged to `main` | No verified post-merge deployment | Controlled test deployment and profile/presence portal smoke test |
| 066A | PR 20 source candidate | No; shared registration intentionally absent | Complete review and merge of the source-only foundation |
| 066A.1 | Planning only | No | After PR 20 and Module 002 merge, create a new isolated branch from then-current `main` for registration-only activation |
| 066B | Not authorized | No | Obtain explicit database design and database-change authorization before implementation |

## Protected global invariants

- Module 059 remains global authenticated application chrome.
- Module 999 remains available through `user-guide`.
- Modules 024–030 and 058 remain registered and navigable.
- Module 056E contract-management behavior remains present.
- Module 062 remains the shared identity and normalized presence authority.
- Current Module 001 and Module 002 workflows are not replaced by new-module work.
- A new route must remain inside the existing authenticated application shell.
