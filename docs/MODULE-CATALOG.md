# ProjectPulse Module Catalog

## Catalog rules

- The catalog records what current source contains and what work is approved.
- `Installed` means the route or global behavior exists on current `main`; it does
  not independently assert deployment or business acceptance.
- `Source candidate` means committed, pushed, and reviewed through an open PR; it
  does not mean merged, registered, deployed, or runtime-active.
- `Release-train candidate` means semantically integrated, validated, committed,
  and pushed through an open draft PR; it does not mean merged, deployed, or
  runtime-active.
- `Runtime-active` requires shared registration, merge, deployment, and successful
  portal verification.
- Proposed retirement or reuse never authorizes removal of current behavior.
- A module number cannot be reassigned until route, API, database, audit, history,
  documentation, and dependency impacts are explicitly approved.
- Customer-facing PDF and Excel artifacts use the approved US Signal logo.

Current deployed baseline: `main@93b519ca54a5322582ed7d33adf91db7ea9e9919`.
It includes the Modules 071/072 runtime repairs and the merged fail-closed Modules
998 and 997 source. Test deployment run `29794000240` verified healthy API/web
revisions, public Modules 071/072 HTTP 200 responses, protected native and
Modules 998/997 HTTP 401 barriers, and both protected web bundles.

## Installed modules on deployed baseline `main@93b519ca54a5322582ed7d33adf91db7ea9e9919`

| Module | Current title | Route/scope | Source state | Governance note |
|---|---|---|---|---|
| 001 | Time Entry | `timesheet` | Installed | Active follow-up work is externally owned; preserve current multiview and save behavior |
| 002 | Approval Inbox | `manager-approval` | Installed through merged PR 23 | Source commit `f5ede8f6717b01c8f4bf7905b433fead38210007`; merge commit `2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4`; preserve PM, correction, password-reset, and history workflows; three post-merge review threads remain unresolved and separately owned |
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
| 026 | CRM/ERP Integration Control Center | `crm-integration` | Native source implemented; migration 034 not applied | SELL, Salesforce, Certinia, ServiceNow, and manually registered platforms; OAuth 2.0/API-key status and audit boundary |
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

## Approved modules and active source work

| Module | Title | Status | Owner/workspace | Dependencies | Number decision |
|---|---|---|---|---|---|
| 061 | Undefined | Scope required | No verified implementation checkpoint | None confirmed | Reserved until explicit scope approval |
| 064 | AI Provider Configuration Center | Shared runtime plus write-only administrator key entry | Module 064 secure provider-key update | Provider governance, encrypted secrets, sanitized audit | Key readback, deletion, and rollback remain unavailable |
| 065 | Entra Secret Administration | Installed fail-closed source through merged PR 24 | Consolidated 064–074 release train | Module 010, Module 062, secure secret controls | No external adapter, secret-store write, durable approval/audit, Azure, or Entra mutation |
| 066 | Project FlowHive | Installed safe 066A.1–066E source through merged PR 24 | Consolidated 064–074 release train | 002, 018, 019, 024–030, 055C, 057, 059, 062, 064 | Database writes, FlowHive AI execution, customer sharing, and deployment remain locked |
| 067 | Global Mail Configuration Center | Installed read-only source through merged PR 24 | Consolidated 064–074 release train | Microsoft 365 readiness and shared outbound-mail ownership | Provider calls, test delivery, secret mutation, and cutover remain locked |
| 068 | System Architecture & Dependency Map | Installed read-only source through merged PR 24 | Consolidated 064–074 release train | Modules 010, 013–017, 058 | No physical discovery or external mutation |
| 069 | Qualifications & Certification Matrix | Installed read-only source through merged PR 24 | Consolidated 064–074 release train | Module 062, existing people/qualification schema | Qualification writes and renewal notifications remain deferred |
| 070 | Capacity & Pipeline Forecasting | Installed source through merged PR 24 | Consolidated 064–074 release train | Modules 020, 057, 062 and existing capacity/request data | Scenario persistence remains locked |
| 071 | On-Call Scheduling | Deployed test source with native persistence and verified public GET access | Current main through PRs 35–36 | Modules 062 and 067; ProjectPulse PostgreSQL | Public bypass is GET-only; native APIs remain authenticated; Cloudflare, scheduler, and mail settings are unchanged |
| 072 | OneAssist Routing PIN Directory | Deployed test source with native persistence, verified public GET access, and JSON-array normalization fix | Current main through PRs 35–36 | ProjectPulse PostgreSQL | Public bypass is GET-only; native APIs remain authenticated; Cloudflare settings and database state are unchanged |
| 073 | Sales Coverage Alignment | Installed unsaved-draft source through merged PR 24 | Consolidated 064–074 release train | Module 062 identity and future audited alignment persistence | Persistence remains locked pending database authorization |
| 074 | OEM & Vendor Directory | Installed unsaved-draft source through merged PR 24 | Consolidated 064–074 release train | Future audited vendor persistence | Persistence remains locked pending database authorization |
| 076 | Defect Intake & Resolution Tracker | Complete source in progress; fail-closed current-main replay | Module 076 integration worktree | Modules 059, 062, 064, 067; ProjectPulse Help; future signed GitHub adapter | Number approved; database persistence, outbox/email delivery, GitHub webhook activation, AI execution, and external mutation remain locked |
| 997 | Security Operations, Threat Intelligence & Response Center | Operational activation source validated on the post-PR-51 baseline; PR and deployment pending | `feature/modules-997-998-operational-response-20260721` | ProjectPulse authentication/session/audit telemetry, Module 998 diagnostic handoff, and approved future security adapters | Native telemetry, durable incidents/timelines, approvals, and gated session revocation are implemented; external Entra/WAF/endpoint/notification/export/AI adapters remain locked |
| 998 | System Diagnostic & Controlled Remediation Center | Operational activation source validated on the post-PR-51 baseline; PR and deployment pending | `feature/modules-997-998-operational-response-20260721` | Module 997 incidents, ProjectPulse PostgreSQL/runtime metadata, Modules 075/077, and approved future Azure/database adapters | Native sessions, findings, runbook previews, approvals, health refresh, and verification are implemented; production-changing adapters remain locked |

Historical PR 24 catalog marker retained for the protected Module 068 validator
(the current Module 067 status is the installed-source row above):

`HISTORICAL_PR24_ROW=| 067 | Global Mail Configuration Center | Release-train candidate`

## Unresolved numbering and reuse decisions

| Candidate | Conflict | Required decision |
|---|---|---|
| Global SMTP historical numbering | Tracker proposed Module 063, but 063 is installed Opportunities | Resolved as Module 067; preserve installed Module 063 |
| Module 005 reuse | Current Project Allocation route remains installed; tracker says retire/reserve | Preserve route and history until a formal retirement plan is approved |
| Module 006 reuse | Current PSA Modules route remains installed | Complete route/API/data dependency audit before reuse |
| Module 011 reuse | Current Work Task Builder route remains installed | Do not replace task behavior with qualifications scope without migration approval |
| Module 020 future scope | Tracker names both Integration Status and Work Intake while current source is Project Intake | Reconcile requirements before any rename or replacement |

## Module 066 phase catalog

| Phase | Outcome | Current state |
|---|---|---|
| 066A | Read-only portfolio, task grid, assignment scope, capability/API contract | Foundation merged through PR 20 as `main@6388f3e3677d9c95380e909d5e5671dcf6fbcf27`; runtime registration remained deferred at that checkpoint |
| 066A.1 | Shared Registration and Activation | Source commit `6e7509cfe9b5704ff291525eb587040f31944ee8` is pushed through open draft PR 24 from `main@2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4`; activation package passed 42/42 checks, frontend build, .NET 10 candidate build, and zero-warning delta |
| 066B | Versioned WBS/dependency/assignment planning contracts and persistence boundary | Source validation and browser-local editing implemented; persistence adapter, schema, baseline approval, collaboration, and audit writes remain locked pending database authorization |
| 066C | Schedule engine, Gantt/timeline, critical path, float, workload, and risk | Deterministic weekday source preview implemented; Module 057 holiday/resource calendars and persisted execution remain gated |
| 066D | GSD/SOW AI request, templates, automations, alerts, API/webhooks | Sanitized Module 064 request and deterministic local template source implemented; provider execution, automations, and external callbacks remain locked |
| 066E | Branded PDF/Excel, customer links, external comment/approval | Internal-draft PDF/XLSX source uses verified US Signal logo; customer links, delivery, and external approval remain locked |

## Current deployment interpretation

| Module | Source status | Runtime-active in portal | Required next step |
|---|---|---|---|
| 062 | Merged to `main` | No verified post-merge deployment | Controlled test deployment and profile/presence portal smoke test |
| 066A | Merged foundation | No; foundation alone did not register the route | Preserve the merged read-only foundation |
| 066A.1–066E | Validated in source commit `6e7509cfe9b5704ff291525eb587040f31944ee8`; open draft PR 24 | No; source is not merged or deployed | Review PR 24 checks and findings; merge and deployment require separate authorization |
| 066B persistence | Locked contract only | No | Obtain explicit database-change authorization before creating a schema, repository adapter, or persistent mutation |
| 064–074 release train | Validated 133-file source commit `6e7509cfe9b5704ff291525eb587040f31944ee8`; pushed in open draft PR 24 | No; the release train is not merged or deployed | Complete PR review and checks; merge and controlled deployment require separate authorization |

## Protected global invariants

- Module 059 remains global authenticated application chrome.
- Module 999 remains available through `user-guide`.
- Modules 024–030 and 058 remain registered and navigable.
- Module 056E contract-management behavior remains present.
- Module 062 remains the shared identity and normalized presence authority.
- Current Module 001 and Module 002 workflows are not replaced by new-module work.
- A new route must remain inside the existing authenticated application shell.

## Module 997 — Security Operations, Threat Intelligence & Response Center

| Field | Current deployed status |
|---|---|
| Number | 997 |
| Historical workspace | `/home/ahmed/project-time-platform-module-997-integration-20260721` |
| Branch | `integration/module-997-current-main-20260721` |
| Source | Recovery `fc4dafa34783fd6b8f5557e7feee8f7626d86766`; integration `6dc90425371b032969d539fe5158892c40a6b268` |
| Status | PR 38 merged as `93b519ca54a5322582ed7d33adf91db7ea9e9919`; CI `29792880067` and deployment `29794000240` succeeded |
| Dependencies | Modules 010, 012–017, 037, 058, 059, 062, 064, 067, 068; deployed Module 998 remains independent |
| Locked boundary | External threat feeds, Entra/WAF/endpoint containment, AI, notifications, evidence export, secrets, and every unapproved external adapter |

Module 997 preserves deployed Module 998 registrations. The operational
activation adds ProjectPulse-native telemetry, incident persistence, separated
containment approval, and the Module 998 diagnostic handoff without enabling an
unapproved external adapter.

## PROJECTPULSE_NATIVE_POSTGRESQL_MIGRATION_031

- Source parent: `603538ad408b70b3e6a26ff2f4f162599fa1cabf`
- Migration source: `database/migrations/031_modules_071_072_native_persistence.sql`
- Rollback source: `database/rollback/031_modules_071_072_native_persistence_rollback.sql`
- Module 071 persistence: ProjectPulse PostgreSQL schedule, roster, acknowledgement, and history tables
- Module 072 persistence: ProjectPulse PostgreSQL routing directory and immutable revision tables
- Platform Administrator authority: explicit
- View-As write authority: blocked
- External compatibility runtime dependency: removed
- Migration applied: no
- Database changed: no
- Deployment performed: no

## PROJECTPULSE_NATIVE_ADMINISTRATION_MIGRATION_032

- Release train: Modules 064–074 native administration, Checkpoint B2
- Parent commit: `42ba87d43526dc9f4a052ca9938473091427cf2a`
- Native document migration: `database/migrations/032_projectpulse_native_administration_documents.sql`
- Reviewed rollback: `database/rollback/032_projectpulse_native_administration_documents_rollback.sql`
- Modules covered: 064, 065, 066, 067, 068, 069, 070, 073, and 074
- Administrator and Super Administrator authority: explicit
- Existing delegated editor roles: preserved by module
- View-As mutation authority: blocked
- Usable secret values: rejected
- Entra, Key Vault, AI-provider secrets, SMTP, and external-system activation: none
- `MIGRATION_032_APPLIED=NO`
- `DATABASE_CHANGED=NO`
- `DEPLOYED=NO`

## Modules 075 and 077–080 current-main source integration — 2026-07-21
<!-- MODULES_075_080_CURRENT_MAIN_INTEGRATION_20260721 -->

The reviewed, fail-closed source packages were integrated sequentially after earlier-module work completed. They are present on current `main`, but no shared runtime registration, connector activation, external mutation, database change, or deployment is authorized by these merges.

| Module | Name | Source PR | Merge commit | Runtime status |
|---|---|---|---|---|
| 075 | Integration Automation & Event Gateway | #29 | `ca4b45bd8b248bd9eb2a69bfa663fd42f3ea7d97` | Source integrated; connectors and mutations locked |
| 077 | Release, Deployment & Rollback Control Center | #30 | `45ac799e1a24a82439a9275add81b3cb60b68464` | Source integrated; deployment and rollback locked |
| 078 | Observability, SLO & Application Health Center | #31 | `8130d65723cc1a10b4c275233dd75779663506e2` | Source integrated; telemetry and alert delivery locked |
| 079 | Data Governance, Retention & Privacy Center | #32 | `0f87d3a863948b95425ec90d7f940734c8c2f55b` | Source integrated; retention, export, and deletion locked |
| 080 | Customer Delivery & Acceptance Portal | #33 | `4e73e729b075e10508c11172724fb5d91a0e0905` | Source integrated; external identity and sharing locked |
