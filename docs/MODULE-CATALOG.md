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

## Installed modules on current source baseline `main@ed76eae30f6b69c97ca597b8926b8bd1f675942b`

| Module | Current title | Route/scope | Source state | Governance note |
|---|---|---|---|---|
| 001 | Timesheet | `timesheet` | Installed; enhanced source candidate | Preserve the current shared weekly draft and five working views; this branch adds real task association, Start / Stop Timer, Mobile mode, and explicit Module 002 submission without changing technical Time Entry identifiers |
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
| 034 | Dashboard and Navigation Labeling | Global enhancement; no standalone route | Installed on `main` | Displays module numbers on dashboard cards and page names in navigation/workspace headers; not a separate module page |
| 035 | Guided Project Intake Launch | Embedded in Module 020 `project-intake`; no standalone route | Installed on `main` | Guided intake launch workflow inside Module 020; not a separate dashboard card |
| 036 | Sales Insights Dashboard | `sales-insights` | Installed | Sales-to-delivery readiness |
| 037 | Roles and Permissions Matrix | `roles-permissions-matrix` | Installed | Least-privilege governance dependency |
| 038 | Certify Integration Center | `certify-integration` | Installed | Expense integration foundation |
| 039 | Billing Readiness Center | `billing-readiness` | Installed | Billing readiness foundation |
| 040 | Project Closeout Center | `project-closeout` | Installed | FlowHive closure dependency |
| 041 | Closeout Email Automation Center | `closeout-email` | Installed | Real email remains governed separately |
| 042 | Invoice & Billing Center | `invoice-billing-center` | Installed route | Open PR 12 is older-lineage work and is not automatically safe to merge |
| 055B | Rate Card Administration | `rate-card-administration` | Installed | Commercial data remains restricted |
| 055C | Manage Existing Projects | `work-register` | Native split deployed to test; migration 036 pending | Assigned PM editing only; PTC, Administrator, and Super Administrator edit every project; mandatory Audit-tab evidence; selected-project closeout handoff opens Module 040 |
| 055D | Create New Project | `create-work-register` | Native split deployed to test; migration 036 pending | PTC, Administrator, and Super Administrator GSD or SELL creation; SELL is authoritative for project name and Actual Rate / Pricing / Rate Review |
| 056E | Contract-management evolution guard | Cross-cutting source invariant | Protected | Preserve 056E fragments and keep forbidden 056D fragments absent |
| 057 | Resource & Team Calendar Capacity | `calendar-capacity` | Installed | FlowHive calendar and workload dependency |
| 058 | Autonomous CI/CD Foundation | `cicd-pipeline` | Installed | Restored registry/navigation on current main |
| 059 | Global Session Intelligence | All authenticated routes | Installed global behavior | Must remain mounted once on every existing and future module route |
| 060 | Contracts & Block of Hours | `contracts` | Installed | Includes contracts/prepaid management foundations |
| 062 | Unified Identity Profile and Presence | `/api/identity/profile`; Profile, Module 057, and Module 059 consumers | Installed through merged PR 19 | Final head `3852a21e1098de9ad907e3da91e0646d99adcb7c`; merge commit `04fcafd4f49840428645e537db7de436e34b1c88`; post-merge test deployment and portal verification remain pending |
| 063 | Opportunities & Action Tracker | `opportunities` | Installed | Current installed identity owns 063; planned SMTP work must receive another number |
| 999 | ProjectPulse Complete User Guide | `user-guide` | Installed | Global documentation route restored on current main |
