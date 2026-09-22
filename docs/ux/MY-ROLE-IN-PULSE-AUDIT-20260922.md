# My Role in Pulse: role-scoped animated user stories

Source baseline: `e81b458f77c6af114c06585475386568a060450d` (main, September 22, 2026).

## Outcome and scope

Replace the all-role educational browser with an effective-user-only experience at `#my-role-in-pulse`, retaining its existing Module 999 ownership. The visual walkthrough is learning content, not a live project dashboard. A user can read only their assigned stories in this page, including server-derived educational assignments. Multiple legitimate roles stay separate. A Super Administrator sees the Super Administrator story, not an unrestricted role picker. Existing Super Administrator operational authority is not reduced.

This is a source-level role and module inventory, plus a targeted implementation review of the story page, role catalog, routing plugin, permission/navigation projection, server-derived journey assignments, and Module 025 ownership/capability contracts. It is NOT an exhaustive line-by-line audit of every backend implementation, a penetration test, a deployed-state inventory, or authenticated acceptance testing of all modules.

## What the repository currently contains

The frontend is a React application built through Vite and repository-owned generation/injection scripts. The User Story route is integrated through `scripts/role-journeys-vite-plugin.mjs`, rather than a new standalone application. The existing guide and generated application boundaries are preserved.

The frontend registry contains **72 registered modules**. The canonical `ROLE_GUIDANCE` contains **12 roles**, while the baseline educational catalog contains **15 playbooks and 60 steps**, including legacy/support role variants. This change preserves that baseline and composes expanded assigned-only stories over it. With all known story roles explicitly assigned for a content audit, the expanded catalog contains **16 playbooks and 72 steps**, including a source-supported Solution Architect Manager playbook. These counts do not imply that a normal user receives all stories or that every registered capability is deployed and operational.

### Complete registered-module inventory

| Area | Registered modules and current display names |
| --- | --- |
| Time Management | 001 Timesheet; 001A Engineer Request Closeout; 001B Time Reallocation & Corrections; 004 Holiday Administration; 023 Time Compliance; 028 AI Time Entry |
| Approvals | 002 Approval Inbox; 007 Approval, Export & Audit Workflow |
| Resource Management | 003 Utilization; 057 Calendar & Capacity; 070 Capacity & Pipeline Forecasting |
| Project Management | 005 Project Expense Upload; 018 Project Workload |
| Project Delivery | 019 Project Engineering Workspace; 020 Project Intake & Resource Handoff; 027 Signed Handoff; 033 Project Forge; 066 Project FlowHive; 082 Enterprise Project Risk Register |
| Sales & Opportunities | 006 Toyota & Hyundai Pipelines; 024 Sales Intake; 025 SOW & GSD Workspace; 036 Sales Insights Dashboard; 063 Opportunities; 073 Sales Coverage Alignment; 074 OEM & Vendor Directory |
| Customers | 021 Customer Directory |
| Reports & Workflow | 022 Cost Alerts; 030 Analytics Center; 031 Financial Operations Workbench; 032 Notification Delivery Monitor; 039 Billing Readiness; 040 Project Closeout; 041 Closeout Email Automation; 042 Invoice & Billing Center |
| Project Operations | 055B Rate Card Administration; 055C Manage Existing Projects; 055D Create New Project; 060 Contracts; 080 Customer Delivery & Acceptance Portal |
| Administration | 009 User Administration; 010 Azure / Entra Directory Users; 012 Role Administration; 037 Roles & Permissions Matrix |
| Integrations | 026 CRM / ERP Integration Center; 038 Certify Connection & Sync Center; 065 Microsoft Integration Connection |
| AI & Automation | 011 Celar AI |
| Security | 064 AI Provider Configuration Center |
| Security & Audit | 008 Audit History; 079 Data Governance, Retention & Privacy Center; 997 Security Operations, Threat Intelligence & Response Center |
| Resources | 069 Qualifications & Certification Matrix |
| Platform Operations | 013 System Health & API Diagnostics; 014 Backup & Disaster Recovery; 015 Restore Validation; 016 Operational Evidence & Backup Retention; 017 Replication & Sync; 029 UAT Validation; 058 CI/CD Pipeline; 067 Global Mail Configuration Center; 068 Provider-Neutral System Architecture; 071 On-Call Scheduling; 072 OneAssist Routing Directory; 075 Integration Automation & Event Gateway; 077 Release, Deployment & Rollback Control Center; 078 Observability, SLO & Application Health Center; 081 Lab Equipment Tracker; 083 Full Future Loop; 998 System Diagnostic & Controlled Remediation Center |
| Help & Documentation | 076 Defect Intake & Resolution Tracker; 999 System User Guide (also owns My Role in Pulse) |

The new expandable workspace map consumes the authorized registry result at runtime. Its module names, groups and descriptions are not copied from the earlier concept image. Module 020 is intake/resource handoff; **055D creates projects and 055C maintains existing projects**. Module 026 remains the CRM/ERP Integration Center and includes the ConnectWise SELL workflow; Module 065 remains Microsoft Integration Connection. No routes or module identifiers are renamed in this PR.

### Role coverage and boundaries

| Role | Primary story and use cases | Scope/boundary |
| --- | --- | --- |
| Sales / Account Executive | Customer need, scoped proposal, commercial handoff, customer follow-up | Assigned customers; does not approve technical scope or unconfirmed delivery capacity |
| Inside Sales | Intake completeness, commercial references, readiness, returned handoff | Assigned customers and authorized sales records; no implied technical approval |
| Solution Architect | Requirements; Plan, Design, Implement, Validate, Release; task-hour review; confirmation; package lifecycle and coverage; handoff receipts | Assigned engagements; AI output remains a draft; operations depend on explicit workspace capability |
| Solution Architect Manager | Scoped team demand, authorized coverage/transfer, review readiness, receiving-owner acknowledgement | Source-supported manager role spellings; reporting access may be read-only; not an automatic author or approver |
| Project Manager | Accept handoff; authoritative project creation; phase-aligned FlowHive WBS; delivery risks; project-time/cost exceptions; closeout | Assigned projects; module access does not imply approval-stage authority |
| Project Management Lead | Portfolio review, escalations, recovery coordination, results | Managed PM team and authorized portfolio |
| Engineer | Assigned documents/tasks, actual-work time entry, own submission, technical evidence, eligible request closeout | Own records and assignments; does not close the whole project or submit/approve for others |
| Engineering Lead | Demand, technical assignment coordination, execution support, technical acceptance evidence | Authorized functional team; approvals remain separate |
| Manager | Demand/capacity, staffing, authorized SOW team work, submitted-time review, utilization exceptions | Direct and indirect reports plus server-enforced module scope |
| Project Team Coordinator | Time stewardship, supported corrections, replacement tasks, administrative reallocation, reconciliation | No submitting another person's timesheet; Module 001B reallocation differs from worker-return/resubmission workflows; audit evidence preserved |
| Project Coordinator | Delivery information, document completeness, follow-ups, PM handoff | Does not inherit the Project Team Coordinator's time-steward authority |
| Accounting | Eligible records, billing-readiness reconciliation, governed export, downstream outcome | Authorized financial information and approval-stage rules |
| Billing / Finance | Billing readiness, billing basis, granted billing actions, exception resolution | Does not inherit engineering, time-entry or platform configuration access |
| Executive | Indicators, source-backed exceptions, accountable decisions, outcome review | Normally read-only organization-level reporting; no implied operational approval |
| Administrator | Approved access requests, delegated changes, validation and audit | Distinct from the Super Administrator playbook |
| Super Administrator | Role/policy governance, Module 064 AI configuration, integration diagnostics, effective-result verification | Operational Full Control preserved; View-As, change approval and environment boundaries remain intact |

There is no separate Procurement or generic Stakeholder story invented from the concept image. New/unrecognized assigned roles receive an explicit guidance gap, not another role's authority. SA disciplines remain within the application's existing SA assignment and reporting controls, rather than creating new permission roles from UI labels.

## Findings and corrections

1. **All-role exposure:** `roleCatalog()` built the full catalog and merely sorted assigned roles first. The old page rendered that full catalog. The new `assignedRolePlaybooks()` filters to verified effective assignments before selection, search, content rendering or examples.
2. **Pre-verification fallback:** the old hook could use session/local role descriptions while navigation was loading. The new audience projection withholds stories and links until server-backed navigation is ready. Explicit empty assignments do not fall back to a privileged role.
3. **View-As and session transitions:** an in-memory identity guard rejects the previous navigation object after an identity change, including two View-As users with identical role sets. UI learning state is synchronously remounted when identity/scope changes. No credential/identity values are put in rendered snapshots or persisted learning state.
4. **Separate assignment from authorization:** the server can add an educational PM journey when a user owns active projects (`ScopedRolePolicyPersistence.cs`). This does not create an app role or module grant. The new implementation retains this distinction and uses the existing module-directory authority for links.
5. **Concrete missing touchpoints:** expanded assigned-role stories cover project creation ownership, PM time review, engineer request closeout, SA package lifecycle/coverage, manager team-work boundaries, PTC time stewardship, and Super Administrator provider/integration governance.
6. **Graphical experience:** local SVG illustrates inputs, the role's action and handoff. Animated connectors and moving document art have a pause control. Optional walkthrough playback advances one step every eight seconds and stops at the last step. Manual selection pauses playback; hidden tabs and reduced-motion preference stop automatic advancement. No remote animation asset, new runtime dependency, AI inference or fake customer metric is introduced.
7. **Accessible fallback:** text contains the same process meaning as the decorative SVG. Keyboard-operated buttons, manual Previous/Next controls, selected-step semantics, focus management, light/dark theme variables, responsive layout, forced colors, print and reduced-motion CSS are retained or added. Accessibility intent follows W3C SC 2.2.2 (https://www.w3.org/WAI/WCAG22/Understanding/pause-stop-hide.html); no formal conformance certification is claimed.

## Source evidence reviewed

All paths are relative to the repository at the baseline above:

- `src/frontend/project-time-web/src/role-journeys/MyRoleInPulse.jsx`
- `src/frontend/project-time-web/src/role-journeys/role-journeys.js`
- `src/frontend/project-time-web/src/role-journeys/use-role-journey-context.js`
- `src/frontend/project-time-web/src/role-journeys/role-journeys.css`
- `src/frontend/project-time-web/src/role-permission-model.js` (role guidance, scopes and action definitions)
- `src/frontend/project-time-web/src/module-availability-registry.js` (all 72 registry entries)
- `src/frontend/project-time-web/src/effective-role-authority.js`
- `src/frontend/project-time-web/src/module-directory-authority.js`
- `src/frontend/project-time-web/src/module-availability-bridge.js` (navigation publication, refresh and server-backed audience projection)
- `src/frontend/project-time-web/scripts/role-journeys-vite-plugin.mjs`
- `src/frontend/project-time-web/tests/role-journeys.test.mjs`
- `src/frontend/project-time-web/package.json`
- `src/backend/ProjectTime.Api/Modules/ScopedRolePolicyPersistence.cs` (effective identity and educational PM assignment)
- `src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs` (role families, endpoint registration, manager and View-As capability boundaries)
- `.github/pull_request_template.md`

## Validation and acceptance

Executed before publishing: 14 Node access/identity-projection tests passed. TypeScript transpile diagnostics found no syntax errors in the changed JS/JSX files. This is syntax validation, not a full application build or browser rendering test.

The PR adds 14 additional catalog/coverage/UI-source tests and a least-privilege, SHA-pinned `Role Journey Visual Checks` workflow running both new suites and the existing role-journey suite:

```sh
cd src/frontend/project-time-web
node --test tests/role-journeys.test.mjs tests/role-journey-access.test.mjs tests/role-journey-experience.test.mjs
```

Repository CI results must be read from the PR, not inferred from local syntax checks. Full application build and authenticated visual UAT remain required before deployment. In UAT, verify every role family, multi-role assignment, unknown/no-role state, revoked access, same-role/different-user View-As, logout/login, denied direct workspace access, light/dark themes, keyboard/screen reader behavior, narrow screens, reduced motion and pause/resume. Inspect the actual DOM for absence of unrelated story panels. Confirm normal owning APIs continue to reject unauthorized record/action requests.

The role catalog is static educational source included in the frontend bundle. This PR enforces the requested **in-app story visibility**, not secrecy of the shipped help text against a user inspecting JavaScript source. Sensitive business data and executable permissions remain protected by the existing owning APIs. Server-delivered confidential documentation would require a separately scoped backend change.

## Release boundaries

New review PR only. No merge, deployment, database migration, provider-order change, secret change, DNS/firewall change, Oracle runtime change, or release-controller modification is part of this work. The new workflow only reads source and runs tests; it has no deployment or write authority. Protected UAT and Production remain untouched by this PR. Revert the PR commit to roll back the UI change; no database rollback is required. Process-owner review of instructional wording remains required.
