# ProjectPulse August Production Readiness Tracker

## Completed Production Readiness Foundation

### Modules 064–074 — Current-main release train

This release train is based on
`main@2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4`. Shared registrations are
integrated once in source commit
`6e7509cfe9b5704ff291525eb587040f31944ee8`, which is pushed through open draft
PR 24. The train remains unmerged, undeployed, and not portal-verified.

| Module | Requirement/outcome | Source readiness | Locked production boundary |
|---|---|---|---|
| 064 | `AI-017` shared AI provider configuration | Claude → OpenAI → local router and read-only center integrated | Secret mutation and live-provider readiness assertions |
| 065 | `RBAC-018` Entra Secret Administration | Read center and fail-closed lifecycle routes integrated | External adapter, step-up middleware, durable approvals/audit, secret-store and Entra mutation |
| 066 | `GOV-015`, `RBAC-019`, `WRK-011`, `AI-008`, `AI-019`, `RPT-013` Project FlowHive | Complete safe 066A.1–066E source registered | Database persistence, FlowHive provider execution, customer sharing |
| 067 | `OPS-016`, `CLS-005` Global Mail Configuration | Read-only configuration and health center integrated | Provider calls, test delivery, secret rotation, cutover |
| 068 | `OPS-013` System Architecture & Dependency Map | Read-only architecture and dependency center integrated | Physical discovery and external mutation |
| 069 | `RES-007`–`RES-012` Qualifications & Certification Matrix | Identity-backed read-only matrix integrated | Qualification writes and renewal notifications |
| 070 | `RES-013`, `RES-014`, `RPT-007` Capacity & Pipeline Forecasting | Identity dropdown, editable dates, and audited calculation model integrated | Persistent scenario writes |
| 071 | `RES-015` On-Call Scheduling | Authenticated center and versioned public GET APIs integrated | Cloudflare configuration/credentials, scheduler activation, and mail delivery |
| 072 | `RES-016` OneAssist Routing PIN Directory | Public unmasked PIN center and versioned GET APIs integrated | Cloudflare configuration/credentials and data migration |
| 073 | `SAL-002` Sales Coverage Alignment | Role-aware unsaved draft center integrated | Audited database persistence |
| 074 | `SAL-003` OEM & Vendor Directory | Role-aware unsaved draft center integrated | Audited database persistence |

`MODULE_064_STATUS=SOURCE_COMMITTED_DRAFT_PR_24_OPEN`

`MODULE_065_STATUS=SOURCE_COMMITTED_DRAFT_PR_24_OPEN_FAIL_CLOSED`

`MODULE_066_STATUS=SOURCE_COMMITTED_DRAFT_PR_24_OPEN_EXTERNAL_LOCKS`

`MODULE_067_STATUS=SOURCE_COMMITTED_DRAFT_PR_24_OPEN_READ_ONLY`

`MODULE_068_STATUS=SOURCE_COMMITTED_DRAFT_PR_24_OPEN_READ_ONLY`

`MODULE_069_STATUS=SOURCE_COMMITTED_DRAFT_PR_24_OPEN_READ_ONLY`

`MODULE_070_STATUS=SOURCE_COMMITTED_DRAFT_PR_24_OPEN_READ_ONLY_SCENARIO`

`MODULE_071_STATUS=SOURCE_COMMITTED_DRAFT_PR_24_OPEN_COMPATIBILITY_ADAPTER`

`MODULE_072_STATUS=SOURCE_COMMITTED_DRAFT_PR_24_OPEN_COMPATIBILITY_ADAPTER`

`MODULE_073_STATUS=SOURCE_COMMITTED_DRAFT_PR_24_OPEN_UNSAVED_DRAFT`

`MODULE_074_STATUS=SOURCE_COMMITTED_DRAFT_PR_24_OPEN_UNSAVED_DRAFT`

#### Release gates

- Monitor draft PR 24 review findings and GitHub checks.
- Obtain separate authority for merge, deployment, database changes, Azure/Entra
  changes, Cloudflare changes, mail activation, or external sharing.
- Keep Module 065 fail-closed and all Module 066 persistence/provider/customer
  boundaries locked until their specific authorization gates are satisfied.

#### Current local validation evidence

- Module contracts passed: 064 43/43, 065 76/76, 066 88/88, 067 57/57,
  068 46/46, 069 54/54, 070 65/65, 071 50/50, 072 52/52, 073 42/42,
  and 074 45/45.
- Protected Module 002, Module 056E, Module 059, and Module 062 validators passed.
- The exact production frontend chain and Vite bundle passed; Module 059 covers
  all 58 registered authenticated routes.
- Whitespace and secret-value scans passed. The reviewed 133-file source set is
  committed as `6e7509cfe9b5704ff291525eb587040f31944ee8`, pushed, represented by
  open draft PR 24, unmerged, and undeployed.
- Module 002 overlap is limited to additive semantic changes in `Program.cs`,
  `App.jsx`, and `package.json`.
- Aggregate .NET 10 baseline/candidate builds passed with zero new warnings, and
  the Module 066 executable suite passed before the release-train commit.

### Module 066 — Project FlowHive

- Consolidated 066A.1–066E source was validated from
  `main@2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4` and committed as
  `6e7509cfe9b5704ff291525eb587040f31944ee8` in open draft PR 24.
- Shared endpoint, role-aware route/navigation, installed-module registry, and
  protected frontend build wiring are integrated in the committed source branch.
- WBS hierarchy, FS/SS/FF/SF dependencies, lead/lag validation, deterministic
  weekday scheduling, critical path, total/free float, and timeline source are present.
- Engineer assignments preserve Module 062 identity IDs and dropdown behavior.
- GSD/SOW AI request preparation uses the integrated Module 064 contract only;
  FlowHive provider execution remains deliberately locked.
- Internal-draft PDF and Excel source embeds the approved US Signal logo.
- Module 002, Module 056E, Module 059, and Module 062 preservation gates remain required.
- No database schema or write, Azure/Entra change, provider call, customer link,
  merge, deployment, or portal activation is claimed.

#### Remaining production gates

- Review draft PR 24 findings and GitHub checks; preserve the validated source boundaries.
- Obtain database authorization before enabling versioned persistence or baseline writes.
- Implement and validate a reviewed FlowHive adapter to the Module 064 router
  before enabling AI-provider execution.
- Obtain external-sharing approval before customer links, delivery, or external comments.
- Merge, deploy, and perform portal acceptance only under separate authorization.

### 019M-O Time Compliance & Notification Center
- Preflight Time Compliance page created.
- Session-authenticated API calls fixed.
- Route isolation fixed.
- Weekly reminder default shown as Monday 6:00 AM Central.
- Weekly escalation default shown as Monday 8:00 AM Central.
- Holiday reminder windows shown for 7-day and 1-day reminders.
- Manager and Project Team Coordinator copy visibility working.
- Preflight notification history working.
- Real send intentionally locked.

## Come Back / Production Hardening Items

### Time Compliance
- Replace UI-only month-end selector with persisted settings.
- Add editable reminder templates backed by database.
- Add real notification provider integration only after approval.
- Add notification approval workflow before real send.
- Add notification_log entries in addition to notification_outbox history.
- Add deduplication controls so repeated preflight validations are grouped by run ID.
- Add run batch table for notification preview/send batches.
- Add export of preflight validation preview to CSV/PDF.
- Add real timezone scheduling logic for Central Time.
- Add business-day adjustment for holiday reminders that fall on weekends.
- Remove duplicate Ahmed production readiness identities or mark one inactive after testing.

### Role Enforcement / User Switcher
- Confirm all roles and permissions are database-driven.
- Add admin-safe user switcher for production readiness mode only.
- Add visible role banner showing current user, role, and permissions.
- Add route guard messaging when a user lacks access.
- Add audit entry when production readiness user switcher is used.

### Project Intake / Resource Assignment
- Add Project Intake shell.
- Add Engineering Resource Request workflow shell.
- Add PM request submission.
- Add manager/resource assignment review.
- Add project workspace shell.
- Add seeded sample clients, projects, tasks, and assignments.

### Expense / Emburse Certify Foundation
- Add expense shell.
- Add Certify import placeholder.
- Add sample expenses.
- Add approval/reporting visibility.

### Reporting & Accountability Dashboard
- Add executive dashboard shell.
- Add missing time, approvals, utilization, expense, and project health cards.
- Add drill-down links to source modules.

### Backup / DR / Restore / Replication
- Keep current production readiness modules.
- Add clearer readiness badges.
- Add exportable DR readiness summary later.

### Help Center
- Add article index.
- Add per-module help article links.
- Add admin support guidance.

## Items Not in August Production Readiness Scope
- Full production Azure migration.
- Salesforce API integration.
- Outlook calendar sync.
- Multi-server communication.
- Real email sending.

## 019M-P Project Intake + Engineering Resource Request

### Production Readiness Foundation Added
- Project Intake route/page shell.
- Engineering Resource Request route section.
- Production Readiness intake records.
- Production Readiness client/project/task records.
- Production Readiness resource profiles, skills, capacity plans, and assignment readiness.
- Help Center article placeholder.

### Come Back / Production Hardening Items
- Persist full settings and workflow transitions.
- Add intake approval status workflow.
- Add manager approval for assignments.
- Add conversion from approved intake into project workspace.
- Add Salesforce opportunity integration later.
- Add Outlook calendar/resource sync later.
- Add export and audit detail views.

### Project Intake Source Handling
- Project intake supports manual entry, manual document upload, and Salesforce source references.
- Salesforce source support stores source system, external reference ID, record type, and source URL.
- Manual upload support stores intake document metadata and file path.
- Future production item: connect Salesforce API/OAuth and field mapping.
- Future production item: add document scanning, retention, and extraction/parsing workflow.

### Project Document Handling
- Intake documents are production-shaped project artifacts, not temporary uploads.
- SOW, GSD, quote/proposal, order form, architecture/design, and other supporting documents are supported.
- Documents may be uploaded for Salesforce-sourced, manual-upload, or manual-entry intake.
- Documents can be marked visible to engineering.
- SOW/GSD can be marked for future AI-assisted timesheet description context.
- Future item: expose these documents on the engineering assignment page and project workspace.
- Future item: add document extraction and AI context summarization pipeline.
- Future item: add safe Claude integration behind provider abstraction.

## 019M-Q Project Workspace + Engineering Documents

### Production Readiness Foundation Added
- Project Workspace route/page shell.
- Project documents panel.
- Engineering-visible SOW/GSD/supporting documents.
- Download links for uploaded documents.
- Timesheet description assistant readiness indicators.
- Assignment visibility for engineers.

### Come Back / Production Hardening Items
- Add document scanning before files are downloadable.
- Add document retention policy and purge workflow.
- Add document preview.
- Add project document versioning.
- Add AI context extraction from SOW/GSD.
- Add approved Claude/provider abstraction for timesheet description assistance.
- Unify intake documents and project document files under a common project document artifact model.

### Staffing Allocation Come Back Items
- Add hard validation so manual allocations cannot exceed requested hours unless override is approved.
- Add hard validation so manual percentages cannot exceed 100% unless override is approved.
- Convert assigned engineering resource requests into project assignment records when the request is formally approved.
- Tie allocations to weekly capacity planning instead of only request-level hours.
- Add manager approval before resource assignments become final.

## 019M-R Role Foundation + Intake Queue/Search UX

### Added
- Role foundation normalized for Engineer, Manager, Project Management, Engineering Team Lead, Project Management Team Lead, Project Team Coordinator, Administrator, and Executive.
- Added future scope permissions for assigned-project, managed-project, team, and executive organization visibility.
- Project Intake queue now supports search, status filter, selected intake dropdown, and latest-20 default display.
- Engineering Resource Request queue now supports search, selected request dropdown, and latest-20 default display.

### Scope Enforcement Come Back
- Engineers should only see projects, tasks, documents, resource requests, and assignments tied to their user ID.
- PMs should only see projects where they are assigned as PM.
- Engineering Team Leads should see only engineers and projects within their team scope.
- Project Management Team Leads should see only PM team scope.
- Managers should see their reporting/team scope.
- Project Team Coordinator should retain broader operational coordination across accounting, billing, expense, reporting, project assignments, and limited role coordination.
- Executives should see organization-wide utilization and reporting by organization, team, manager, and individual.

## 019M-S Role Scope Foundation + Intake Queue UX

### Added
- Preserved original role scopes and layered in Engineering Team Lead, Project Management Team Lead, and Executive.
- Added role scope rules table for future backend enforcement.
- Added team scope assignment table for manager/team lead mapping.
- Improved Project Intake queue with search, status filter, selected-intake dropdown, and latest-20 default view.
- Improved Engineering Resource Request queue with search, selected-request dropdown, and latest-20 default view.

### Next
- 019M-T Backend role scope enforcement:
  - Engineer = assigned-self only.
  - PM = managed projects only.
  - Engineering Team Lead = engineering team only.
  - PM Team Lead = PM team only.
  - Manager = reporting/team scope.
  - Project Team Coordinator = broad operational scope.
  - Executive = organization-wide read/reporting scope.
  - Administrator = full platform scope.

## 019M-T Project Workspace Role Scope Enforcement

### Added
- Backend role scope filtering for Project Workspace.
- Engineers only see directly assigned project workspace records.
- PMs see managed project workspace records.
- Engineering Team Leads and Project Management Team Leads see team-scoped records.
- Managers see reporting/team-scoped records.
- Project Team Coordinator, Executive, and Administrator retain broad visibility based on operating/reporting role.
- Project document download endpoint now checks workspace role scope.

### Come Back
- Apply the same backend role scoping to Project Intake.
- Add formal team/lead assignment UI.
- Add Executive utilization dashboard.
- Add Project Team Coordinator billing/accounting/expense/reporting scope review.

## 019M-U Administrator View-As / User Experience Preview

### Added
- Administrator-only user experience preview for Project Workspace.
- Admin can select a user and view role-scoped workspace data as that user.
- Preview mode is read-only by design.
- View-as activity is logged to projectpulse_admin_view_as_audit.
- Workspace displays a visible preview banner and exit option.

### Guardrails
- Only Administrator can use View As User preview.
- Preview does not grant write authority as the selected user.
- Backend still evaluates role scope using the selected effective user.
- Production hardening should extend this pattern to Intake, Time, Approvals, Reports, and Expenses.

## 019M-V Global View-As User Preview

### Added
- Moved Administrator View-As/User Experience Preview to the global top bar layer.
- Selected preview user is stored globally and applies across page navigation.
- All frontend API calls now automatically receive X-ProjectPulse-View-As-User while preview is active.
- Write API calls are blocked in the browser while preview is active to keep View-As read-only.
- Workspace-local View-As panel is hidden because preview is now global.

### Important
- Backend modules must honor X-ProjectPulse-View-As-User to apply effective-user scoping.
- Project Workspace already honors this header.
- Intake, Timesheet, Approvals, Utilization, Expenses, and Reports should be wired next through backend scope enforcement.

## 019M-X Global Effective User Scope + Timesheet Ownership Guardrail

### Added
- Backend now resolves X-ProjectPulse-View-As-User globally for Administrator read-only preview.
- Backend blocks write actions while View-As preview is active.
- GetProjectPulseSessionUserId now returns the effective viewed user when View-As is active.
- Timesheet, utilization, navigation/security context, and other APIs that depend on session user now use the effective user.
- View-As activity is audited through projectpulse_admin_view_as_audit.

### Ownership Rule
- Time entries remain owned by user_id.
- Engineers should only see their own timesheet entries.
- Project Team Coordinator and Administrator can operationally select users.
- View-As preview remains read-only and cannot submit or save time as the selected user.

## 019M-Y Role Cleanup + Workspace Allocation Hours

### Added
- Engineers and Project Managers retain holiday visibility but no longer receive holiday management/upload permission.
- Holiday management is limited to Administrator and Project Team Coordinator.
- Engineers and Project Managers no longer receive Project Info / Project Allocation page permissions.
- Project assignments now support assigned hours.
- Project Workspace assignment rows now show assigned hours, used hours, remaining hours, and overrun indication.
- Used hours are calculated from time entries by engineer, project, and task.

### Notes
- Existing project assignment hours are backfilled from current engineering resource request allocations when available.
- Work Task Builder will set assigned hours directly during future PM task assignment.

## 019M-AA Holiday Upload Removal + Engineer Task Bridge

### Added
- Completely hides holiday upload controls for roles without MANAGE_HOLIDAYS.
- Replaces manager wording with Project Team Coordinator / Administrator for holiday management.
- Upgrades holiday rows toward a card-style dashboard view.
- Bridges resource request engineer assignments into project task assignments when project tasks exist.
- Timesheet available tasks are now expected to come from assigned project tasks and include customer, assigned hours, used hours, and remaining hours.

## 019M-AB Repair Timesheet Available Tasks and Holiday Read-Only UI

### Fixed
- Added missing backend route /api/assignments/available-tasks.
- Removed aggressive holiday UI guard that could hide holiday rows.
- Added safer holiday upload guard that hides only file upload controls for read-only users.
- Timesheet assigned task payload includes customer, assigned hours, used hours, and remaining hours.

## 019M-AC Holiday Calendar Cards + Regular Task Mapping

### Fixed
- Removed duplicate holiday read-only messages.
- Removed holiday upload/import instructional copy from read-only holiday views.
- Re-rendered uploaded holidays as calendar-style cards using month, day, weekday, holiday name, type, and hours.
- Regular Tasks now maps to assigned project tasks so engineers can select assigned project work from the timesheet.
- Assigned project tasks remain tied to customer, project, task, assigned hours, used hours, and remaining hours.

## 019M-AF Targeted Holiday JSX and Regular Task Option Fix

### Fixed
- Patched the actual Holiday calendar JSX instead of using DOM overlay renderers.
- Removed the old injected holiday renderer from App.jsx.
- Holiday display now uses the native React holiday data and renders as a 3-column card grid.
- Upload CSV controls render only for Administrator, Manager, and Project Team Coordinator.
- Engineer and Project Management users can view holidays but cannot upload.
- Removed duplicate empty Regular Tasks option while keeping assigned project tasks under Regular Tasks.

## 019M-AG Phase 1 Customer Directory and Intake Cost Foundation

### Added
- Added customer contact table foundation with a maximum of 10 active contacts per customer.
- Added customer linkage and planned engineering, PM, and total project cost fields to project intake.
- Added planned engineering, PM, and total project cost fields to projects.
- Added project cost status view for assigned hours, used hours, remaining hours, and over-plan flags.
- Added read APIs for customer overview and project cost status.

### Not yet changed
- No frontend redesign was added in this phase.
- Intake UI still uses the existing layout until the customer selector and cost-entry UI are patched separately.

### 019M-AG Phase 1 adjustment
- Customer overview now separates project planned cost from intake pipeline planned cost to avoid double-counting converted intake/project records.

## 019M-AG Phase 2 Intake Customer Selector and Planned Cost UI

### Added
- Project Intake now loads customers and contacts from the Customer Directory foundation.
- Intake creation uses a selected customer record instead of relying only on free-text customer entry.
- Selected customer contacts are displayed in the intake form.
- Intake creation captures planned engineering cost, planned PM cost, and calculated total project cost.
- Intake queue displays planned cost values for existing and newly created intake records.

## 019M-AH Customer Directory Management UI

### Added
- Added Customer Directory management permissions for customer viewing and management.
- Added backend endpoints to create/update customers and create/update customer contacts.
- Added Customer Directory screen with customer list, contact list, cost readiness summary, and management forms.
- Added sidebar route for Customer Directory under project operations.
- Preserved the 10 active contacts per customer rule.

## Mobile Readiness Baseline

### Added
- Added a global mobile-readiness stylesheet for Project Pulse.
- Standardized phone behavior for panels, forms, card grids, navigation, modals, and wide table wrappers.
- Preserved internal horizontal scrolling for wide operational grids such as timesheets and reporting tables while preventing full-page horizontal overflow.
- Established mobile responsiveness as a required acceptance check for all new modules going forward.

## 019M-AI Cost Overrun Alert Foundation

### Added
- Added project cost alert persistence for missing cost plans, active projects without cost plans, over-assigned hours, and low remaining assigned hours.
- Added alert evaluation endpoint that records current project cost alert conditions.
- Added notification routing foundation to PM, resource manager, and Project Team Coordinator through existing notification outbox tables.
- Added mobile-friendly Cost Overrun Alerts screen with live candidates, recorded alerts, and evaluation controls.

## 019M-AJ Navigation Usability + View-As Repair

### Added
- Replaced the default left-side workspace navigation with a top navigation pattern.
- Kept role-specific common destinations visible directly across the top.
- Added a top-right More menu for Admin/PTC and other large-menu roles.
- Hid the legacy sidebar so it no longer consumes left-side workspace content area.
- Repaired global Administrator View-As visibility and refresh behavior after login.
- Added mobile guardrails so the More menu opens by tap and does not permanently cover page content.

## 019M-AJ Top Navigation Visual Repair

### Added
- Moved Administrator View-As from the top navigation area to the bottom-right utility area.
- Made the profile dropdown opaque and readable.
- Anchored the profile initials button to the far-right header utility area.
- Changed the More menu to open by click instead of hover.
- Adjusted Engineer/simple-role direct navigation to Timesheet, Utilization, Holidays, and Project Workspace.

## 019M-AK Project Manager Workload Dashboard

### Added
- Removed the Current Quarter Utilization summary from the Dashboard role workspace.
- Added Project Workload as the project-manager-focused replacement for Utilization.
- Hid Utilization from Project Management / Project Manager role navigation.
- Added Project Manager Workload API for active projects this quarter, closed projects this quarter, project status counts, assigned project list, and workload risks.
- Added mobile-friendly Project Workload screen.

## 019M-AJ Cost Alert Acknowledgement + Routing Controls

### Added
- Added cost alert acknowledgement, resolution, reopen, and routing status fields.
- Held cost alert notifications by default during evaluation.
- Added manual notification release controls to prevent accidental duplicate routing.
- Added backend endpoints for alert status updates and notification release.
- Added Cost Alerts UI controls for acknowledge, resolve, reopen, and release notification actions.
- Added routing status, acknowledgement history, release history, and action notes to the Cost Alerts screen.

## 019M-AL Approval / Export / Audit Workflow Foundation

### Added
- Added workflow summary API for manager approval, PM validation, accounting readiness, reconciliation, lock, and export counts.
- Added role-scoped workflow items API.
- Added PM approval, accounting-ready, reconcile, and lock actions.
- Added time export foundation records for Excel/PDF readiness.
- Added audit log records for PM approval, accounting readiness, reconciliation, lock, and export preparation.
- Connected the workflow to the existing role dashboard/module card structure.
- Added mobile-ready Approval / Export / Audit Workflow UI.

## 019M-AL UI Repair: Dashboard Registry, Cost Alert Route, Help Assistant

### Added
- Added dashboard Installed Modules registry with role-based module cards and plain-language descriptions.
- Added Cost Alert route isolation so the Cost Alert Overrun page does not render as an endless scroll under the main dashboard.
- Restored floating Help Assistant visibility above application panels.
- Added mobile readiness for the installed module registry and Cost Alert route.

## 019M-AL UI Repair: Blank Dashboard Recovery

### Fixed
- Replaced the dashboard Installed Modules rendering with a static, safe module registry to avoid blank-page runtime failures.
- Route-gated Cost Alert Overrun directly in JSX so it cannot render under the dashboard.
- Removed the risky Cost Alert CSS route-hiding rule that could hide dashboard content.
- Ensured the Help Assistant component is imported and rendered.

## 019M-AL UI Repair: Project Workload and Cost Alert Route Isolation

### Fixed
- Isolated Project Workload so it opens as its own route instead of stacking under the dashboard.
- Isolated Cost Alert Overrun so it opens as its own route instead of stacking under the dashboard.
- Preserved the Installed Modules dashboard registry and Help Assistant repair.

### Queued
- Project Manager should only see their own Project Workload.
- PM Team Lead should receive a Project Workload dropdown to select PMs on their team.
- PM Team Lead dropdown must be scoped by PM team relationship, not global user visibility.

## 019M-AM Project Workload PM Scope + PM Team Lead Selector

### Added
- Project Manager workload is now backend-scoped to the signed-in PM by default.
- PM Team Lead workload supports a dropdown scoped to PMs on the same team.
- Administrator/PTC workload can still review all PMs or select a specific PM.
- Project Workload API now returns selectable PMs, selected scope, and access flags.
- Project Workload UI now includes a role-aware PM selector when the user is allowed to select PMs.
- Added mobile readiness for the PM workload selector.

## 019M-AN Post-Intake Editability + Signed Date Aging

### Added
- Added Project Signed Date to intake requests.
- Added signed-date aging logic for 7-day reminders, 14-day reminders, and 21-day escalations.
- Added post-intake edit tracking and intake-specific change history.
- Added post-intake supporting document upload/replace workflow.
- Added Project Intake panel for aging review, post-intake updates, and supporting document uploads.
- Updated Project Intake dashboard description to include signed-date aging.
- Added mobile readiness for the post-intake aging panel.

### Role Behavior
- Project Coordinator/PTC/Admin can edit post-intake fields and upload/replace documents.
- PM/PM Team Lead can view intake aging where permitted.
- All changes are captured in project intake change history and audit logs.

## 019M-AO Engineering Team Lead Utilization Scope + Engineer Selector

### Added
- Added backend-scoped engineering utilization endpoint for Engineering Team Leads.
- Engineering Team Leads can view all engineers in their team scope or select one engineer.
- Engineers remain limited to their own utilization scope.
- Admin/PTC can view all engineers or select one engineer.
- Existing Manager Team Utilization behavior remains unchanged.
- Added utilization route panel and mobile readiness for Engineering Team Lead utilization.

### Scope Enforcement
- Backend enforces all team/engineer visibility.
- The UI selector only displays choices already allowed by the API.

## 019M-AP Work Task Builder / Task Classification Foundation

### Added
- Added Work Task Builder foundation for project, service request, open, and non-project task classification.
- Added global work task templates with billing and utilization classification.
- Added scoped project task creation and assignment workflow.
- Project Managers can create and assign work tasks only inside their managed project scope.
- PTC/Admin can manage templates and assign across project scope.
- Engineers continue to receive assigned project tasks through the existing timesheet task selector.
- Added mobile readiness and route isolation for Work Task Builder.

### Classification Foundation
- Task categories: Open Tasks, Project Tasks, Service Request Tasks, Non-Project Tasks.
- Billing classification: Billable, Non-billable.
- Utilization classification: Billable utilization eligible, Non-billable utilization eligible, Non-billable non-utilization eligible.

## 019M-AQ Role Administration Directory + Permission Visibility

### Added
- Added Role Administration Directory summary endpoint.
- Added plain-language role definitions.
- Added assigned team member visibility by role.
- Added permission visibility grouped by module.
- Preserved existing administrator-only role management workflow.
- Added mobile-ready cards and permission chips for role administration review.

### 019M-AQ UI clarity repair
- Clarified Permission Modules Summary so module-count rows are understood as role-permission grant counts.
- Added a separate Role Directory heading before role cards.
- Preserved the existing User Role Administration assignment section below the directory.

## 019M-AR Project Intake to Work Task Builder Handoff Readiness

### Added
- Added Intake to Work Task Builder readiness visibility.
- Shows lifecycle from intake to signed approval, project record, work tasks, engineer assignment, timesheet usage, and utilization readiness.
- Shows direct project links and possible project matches for intake records.
- Shows task readiness, assignment readiness, assigned engineer counts, assigned hours, and timesheet activity.
- Keeps this as visibility/readiness first; automatic intake conversion is intentionally not enabled yet.
- Added mobile-ready handoff cards on the Project Intake route.

## 019M-AS Intake Project Link Confirmation + Resource Assignment Handoff

### Added
- Added a dedicated confirmed intake-to-project link table.
- Added project link management permission.
- Added project link options endpoint with suggested candidate matches.
- Added manual project link confirmation endpoint.
- Updated handoff readiness to treat confirmed links as direct links.
- Confirmation updates unlinked resource requests and intake documents for the selected intake.
- Added Admin/PTC/Project Management controls while keeping engineers out of handoff management.

## 019M-AT Resource Request Assignment to Work Task Assignment Handoff

### Added
- Added resource assignment handoff visibility.
- Shows how engineering resource request assignments relate to project tasks, project task assignments, timesheet activity, and utilization readiness.
- Identifies project-link gaps, work-task gaps, resource-assignment gaps, task-assignment gaps, assignment-hour gaps, and timesheet usage pending states.
- Keeps this as readiness visibility only; automatic promotion from resource request assignment to project task assignment is intentionally disabled.
- Added mobile-ready readiness cards on the Project Intake route.

## 019M-AU Manual Resource Assignment to Project Task Promotion Controls

### Added
- Added manual promotion permission for resource assignment to project task assignment handoff.
- Added management-only endpoint to promote a resource request engineer assignment into project task assignments.
- Promotion requires explicit task selection, assigned hours, effective dates, and a promotion note.
- Existing project task assignments for the same project/task/engineer are updated instead of duplicated.
- Duplicate-risk rows are skipped and reported instead of blindly creating more assignments.
- Promotion writes audit history and keeps assignment_source as resource_request_promotion.
- Added mobile-ready promotion controls to the Resource Request to Work Task Assignment Handoff panel.

## 019M-AV Approval Export Audit Workflow Hardening

### Added
- Added workflow operational readiness permission and endpoint.
- Added workflow stage grouping for manager review, project validation, accounting review, reconciled/locked, and returned/rejected time.
- Added export readiness logic showing ready versus blocked entries for the selected date range.
- Added role guidance for Engineer, Manager, Project Management, PTC/Admin, and Executive workflow behavior.
- Added audit evidence feed for workflow-related audit events.
- Added mobile-ready operational readiness and audit evidence panels to the Approval / Export / Audit Workflow route.
- No workflow status changes are performed by the readiness endpoint.

## 019M-AW / 019M-AX / 019M-AY Export Package, Dashboard Registry, and Audit Evidence Sprint

### Added
- Added export package download permission and export package readiness permission.
- Added workflow audit evidence detail permission.
- Added export package metadata columns for generated timestamp, content type, download count, and last download tracking.
- Added CSV/Excel-ready export package download endpoint.
- Added export package detail endpoint with package metadata, scoped export items, and audit evidence.
- Added export package download controls to the Approval / Export / Audit Workflow route.
- Added dashboard module registry cards for Workflow Operational Readiness, Export Packages, and Workflow Audit Evidence.
- Updated workflow route permissions so read-only Executive/workflow roles can see appropriate dashboard/module cards while export downloads remain restricted.

## 019M-AZ through 019M-BJ Workflow Operations Mega Sprint

### Added
- 019M-AZ Audit History Endpoint + UI Repair.
- 019M-BA Workflow Action Completion Controls and preflight validation workflow planning.
- 019M-BB Dashboard Module Visibility Smoke Automation.
- 019M-BC Export Package Readiness Summary.
- 019M-BD Export Package Evidence Detail registry coverage.
- 019M-BE Accounting Reconciliation Workbench.
- 019M-BF Locked Period Audit Evidence.
- 019M-BG Role Access Matrix Endpoint.
- 019M-BH Production Readiness Command Center.
- 019M-BI Workflow Validation Rules.
- 019M-BJ Workflow Operations Center Registry.
- 019M-BK Sprint Automation Validation Script.

### Notes
- This sprint is intentionally mostly read-only.
- Workflow preflight validation records evidence only and does not change time entry status.
- Dashboard/module registry coverage was added for all new modules.
- Engineer users remain excluded from workflow/export/reconciliation management controls.

## 019M-BL through 019M-BU Production Hardening Sprint

### Production posture correction
- Reframed operational readiness away from production readiness-first language and toward production readiness.
- Replaced preflight validation user-facing language with workflow preflight validation.
- Added production preflight validation evidence table and endpoints.
- Added production readiness command center endpoint.
- Added route permission contract governance.
- Added navigation registry integrity endpoint.
- Added production export evidence endpoint.
- Added production workflow operations UI/data foundation endpoint.
- Engineer users remain excluded from restricted export, reconciliation, route contract, and accounting controls.

### Production endpoint coverage
- `/api/workflow/preflight-validation`
- `/api/workflow/preflight-validation/run`
- `/api/workflow/preflight-events`
- `/api/production/readiness-command-center`
- `/api/security/route-permission-contracts`
- `/api/navigation/registry-integrity`
- `/api/export-packages/evidence-summary`
- `/api/workflow/operations-ui-data`

## 019M-BV Production Preflight Response Naming Cleanup

- Updated the production operations-center response to expose `preflightEvidenceCount` instead of `dryRunEvidenceCount`.
- Pointed production operations-center evidence counting to `workflow_preflight_validation_events`.
- Preserved prior compatibility routes while ensuring production-facing response naming aligns with production preflight terminology.

## 019M-BV Production Module Wording Cleanup

- Updated production operations-center module names to remove production readiness and sprint-only terminology.
- Updated workflow validation rule evidence language from preflight validation wording to workflow preflight validation wording.
- Preserved compatibility behavior while ensuring production-facing API responses use production and preflight terminology.

## 019M-BV Validation Rule Wording Repair

- Updated production validation rule output from preview terminology to workflow preflight validation terminology.
- Updated action capability notes and compatibility action messages so production-facing responses no longer describe workflow safety checks as preview behavior.
- Preserved legacy route and table compatibility while correcting response language used by the application.

## 019M-BW through 019M-CD Production Workflow Operations UI

- Added production UI panels for workflow operations, production readiness, route permission contracts, registry integrity, export evidence, audit evidence, and workflow preflight evidence.
- Dashboard page now surfaces Production Readiness Command Center data.
- Workflow page now surfaces Production Workflow Operations Center data, including preflight validation, evidence, export package evidence, reconciliation readiness, audit events, and validation rules.
- Role Admin page now surfaces Route Permission Contract Center data for role enforcement governance.
- UI respects current session and Administrator View-As headers so restricted endpoints remain restricted under engineer preview.
- No production readiness-first or preview production wording is introduced.


## 019M-BW through 019M-CD Time Compliance Production Wording Repair V2

- Updated Time Compliance UI wording from preview/production readiness language to production notification preview language.
- Preserved compatibility with existing backend response fields where needed.
- Did not require backend route changes because the production issue was frontend bundle wording.


## 019M-BW through 019M-CD Recursive Time Compliance Wording Repair

- Recursively repaired Time Compliance production-facing wording across all frontend source files.
- Replaced visible preview/production readiness language with notification preview and production review terminology.
- Preserved backend compatibility behavior while preventing the built production bundle from exposing old Time Compliance wording.


## 019M-BW through 019M-CD Recursive Time Compliance Wording Repair

- Recursively repaired Time Compliance production-facing wording across all frontend source files.
- Replaced visible dry-run/production readiness language with notification preview and production review terminology.
- Preserved backend compatibility behavior while preventing the built production bundle from exposing old Time Compliance wording.

## 019M-CE Time Compliance PreviewOnly Response Cleanup

- Updated the production-facing Time Compliance preview summary field from `dryRunOnly` to `previewOnly`.
- Preserved notification preview behavior while aligning API response naming with production terminology.
- Added validation to confirm `/api/time-compliance/preview` no longer returns `dryRunOnly`.

## 019M-CE Full Time Compliance Preview Response Repair

- Updated the full `/api/time-compliance/preview` payload so the top-level production response uses `previewOnly` instead of `dryRunOnly`.
- Updated generated notification preview body text from dry-run wording to notification preview wording.
- Added a database migration to update reminder-rule cadence descriptions from dry-run preview wording to notification preview wording.
- Preserved compatibility behavior while removing production-facing dry-run terminology from the preview response.

## 019M-CE Remaining Dry-Run Preview Text Cleanup

- Removed remaining production-facing dry-run wording from Time Compliance preview source messages.
- Rewrote the preview response wording migration so it safely discovers actual notification/reminder/time-compliance tables.
- Updated matching database text columns from dry-run preview language to notification preview language.
- Preserved the compatibility route while cleaning production-facing API and UI text.

## 019M-CF Production Wording + Compatibility Guard Sweep

- Added a full production wording guard validation script for frontend source, backend source, built frontend bundle, API responses, and compatibility routes.
- Confirmed Time Compliance preview uses `previewOnly` at the top level and inside the summary contract.
- Confirmed production-facing API responses do not expose `dryRunOnly`, `Dry-run`, `Production Readiness`, or August production readiness wording.
- Preserved compatibility routes where needed while validating that production responses use preflight/preview terminology.
- Included Dashboard / Navigation / Registry validation for module visibility, navigation registry integrity, and production readiness.
- Included Engineer View-As negative access checks for restricted workflow, export, route contract, registry, and role-matrix endpoints.
- Included public smoke checks for Dashboard, Workflow, Role Admin, and Time Compliance routes.

## 019M-CF Recovery Note

- Repaired the remaining production readiness access-denied message in `Program.cs`.
- Replaced the last production-facing production readiness-readiness wording with production-readiness terminology.
- Reran the full production wording, compatibility, Dashboard, Navigation, Registry, and engineer negative-access guard sweep.

## 019M-CF Login Page Production Panel Auth Guard

- Added an auth-aware guard to the production workflow operations injector.
- Prevented Production Readiness / Workflow Operations panels from rendering or calling protected endpoints before a valid Project Pulse session exists.
- Fixed the login-page issue where `#dashboard` could show HTTP 401 `session_required` cards before sign-in.

## 019M-CF Login Page Auth Guard Scope Repair

- Repaired the production operations injector auth guard so it is available in both module scope and window scope.
- Prevented the signed-out login page from rendering Production Readiness cards or calling protected readiness endpoints.
- Added defensive cleanup for injected production panels when no Project Pulse session exists.

## 019M-CF Clean Auth-Aware Production Operations Injector Repair

- Replaced the production workflow operations injector with a clean auth-aware implementation.
- Production operations panels are now suppressed until a valid Project Pulse session exists.
- The signed-out login page no longer renders protected readiness panels or 401/request_failed cards.
- The injector remains active through MutationObserver, route changes, storage changes, focus events, and periodic sync so panels can appear after sign-in.
- Cleaned dashboard module visibility notes that still exposed legacy non-production terminology in API responses.

## 019M-CF Clean Auth-Aware Production Operations Injector Repair

- Replaced the production workflow operations injector with a clean auth-aware implementation.
- Production operations panels are now suppressed until a valid Project Pulse session exists.
- The signed-out login page no longer renders protected readiness panels or 401/request_failed cards.
- The injector remains active through MutationObserver, route changes, storage changes, focus events, and periodic sync so panels can appear after sign-in.
- Cleaned dashboard module visibility notes that still exposed legacy non-production terminology in API responses.

## 019M-CG Native React Production Operations Panels

- Added `ProductionOperationsPanel.jsx` as the native React implementation for production readiness, workflow operations, and route permission contract panels.
- Removed the side-effect production operations injector import from `App.jsx`.
- Preserved signed-out suppression by returning no panel until a valid Project Pulse session exists.
- Preserved View-As header forwarding for production operations evidence calls.
- Kept Dashboard / Navigation / Registry coverage across readiness, registry integrity, module visibility, workflow operations, and role-admin route governance.

## 019M-CH Local Admin Password Reset Queue Clear Controls

- Added backend clear-summary and clear-ready endpoints for local admin password reset request queues.
- Added dynamic database support for clearing approved/reset-ready records while preserving cleared evidence columns.
- Added Manager Approval route UI control to clear approved local admin password reset requests from the temporary-password action queue.
- Restricted queue clearing to Administrator and Project Team Coordinator roles.
- Added Dashboard / Navigation / Registry validation and engineer negative-access validation coverage.

## 019M-CH Recovery: Direct Password Reset Queue Clear Endpoint

- Removed the failed separate local admin password reset clear module from the active API path.
- Added a safer direct `/api/auth/password-reset/clear-ready` endpoint beside the existing password reset workflow.
- Added a Manager Approval route panel that counts approved local admin password reset requests and clears them from the temporary-password action queue.
- Preserved Administrator / Project Team Coordinator authorization using the existing user-administration access guard.
- Preserved Dashboard / Navigation / Registry validation.

## 019M-CH Visible Password Reset Queue Clear Button Repair

- Repaired the Manager Approval clear control so the button is fixed and visible on the page instead of being hidden below the approval table.
- Connected the control to the existing password reset approvals queue and direct clear-ready endpoint.
- The control clears approved local admin reset requests that are still waiting for temporary-password completion.
- Preserved Dashboard / Navigation / Registry validation.

## 019M-CH Password Reset Clear Count Source Repair

- Repaired the floating reset queue clear panel so its count comes from a backend queue summary endpoint rather than frontend-only approval inference.
- Added `/api/auth/password-reset/clear-ready-summary` to read the true approved local-admin reset queue count.
- Updated `/api/auth/password-reset/clear-ready` so the clear action uses the same queue definition as the summary count.
- Preserved Dashboard / Navigation / Registry validation.

## 019M-CI Production Operations Acknowledgments + Sign-Off Evidence

- Added `production_operations_acknowledgments` evidence table.
- Added production operations acknowledgment summary, event, and sign-off endpoints.
- Added native React sign-off panel for Dashboard, Workflow, and Role Admin production operations routes.
- Preserved View-As read-only behavior by hiding sign-off controls while impersonating another user.
- Added Dashboard / Navigation / Registry validation and engineer negative-access validation coverage.

## 019M-CI 500 Repair Note

- Verified the production operations acknowledgment summary endpoint after deployment.
- Applied safe Npgsql parameter typing and JSONB handling for acknowledgment summary/sign-off evidence if the endpoint was still returning HTTP 500.
- Preserved Dashboard / Navigation / Registry validation.

## 019M-CI Acknowledgment Table Grant Repair

- Repaired production operations acknowledgment endpoints by granting the API database login role access to `production_operations_acknowledgments`.
- Added the grant block to the 019M-CI migration so future deployments preserve table access.
- Confirmed Time Compliance settings and preview endpoints are healthy after deployment.
- Preserved Dashboard / Navigation / Registry validation.

## 019M-CI Session Actor Lookup Repair

- Repaired production operations acknowledgment POST by replacing the hardcoded `app_user_sessions` lookup with dynamic session-table discovery.
- Preserved actor evidence capture when the active session table can be resolved.
- Confirmed the read endpoints were healthy after table grants and focused this repair on sign-off POST evidence creation.
- Preserved Dashboard / Navigation / Registry validation.

## 019M-CJ Time Compliance Automatic Engineer Email Notifications

- Added notification run, delivery event, and schedule-control tables for automatic engineer time-compliance email notifications.
- Added summary, event, and send endpoints under `/api/time-compliance/email-notifications`.
- Added native React controls on the Time Compliance route for outbox-only runs and real sendmail delivery when server delivery readiness is available.
- Preserved preview-before-send behavior by building delivery runs from `/api/time-compliance/preview`.
- Preserved Dashboard / Navigation / Registry validation and engineer negative-access validation coverage.

## 019M-CJ Nullable Response Build Repair

- Repaired nullable `DateTime` and `DateOnly` response fields in the automatic time-compliance email notification endpoints.
- Preserved automatic engineer notification run records, delivery events, schedule controls, and outbox/sendmail readiness behavior.
- Preserved Dashboard / Navigation / Registry validation.

## 019M-CK Shared ProjectPulse Email Provider Configuration

- Added `/etc/projectpulse/email.env` as the single global email provider configuration source for ProjectPulse.
- Added systemd EnvironmentFile wiring through `projecttime-api.service.d/40-projectpulse-email-provider.conf`.
- Added `/api/system/email-provider/summary` to expose non-secret provider readiness and registered email consumers.
- Added `system_email_provider_consumers` non-secret registry for current and future email-capable workflows.
- Refactored Time Compliance automatic notifications to use the shared ProjectPulse email provider instead of owning separate provider settings.
- Preserved outbox-only mode, Brevo API delivery mode, `.local` recipient blocking, Dashboard / Navigation / Registry validation, and Engineer View-As restrictions.

## 019M-CL Shared Email Provider Test Harness

- Added controlled single-recipient shared email provider test endpoint.
- Added provider test event audit table.
- Added confirmation gate `SEND_PROVIDER_TEST` to avoid accidental test sends.
- Preserved `.local` recipient blocking and View-As write protection.
- Preserved Dashboard / Navigation / Registry validation.

## 020J Shared Email Recipient Safety Review

- Added shared recipient safety rules, reviews, and review item audit tables.
- Added recipient safety review endpoints for global email-provider consumers.
- Added Time Compliance recipient review generation from the existing preview data.
- Flags `.local`, production readiness/test, duplicate, invalid, missing manager, non-routable manager/CC, and external-domain risks.
- Blocks real provider batch sends until an approved recipient safety review exists with zero blocked recipients.
- Preserves outbox-only operation, View-As write protection, Dashboard / Navigation / Registry validation, and provider test harness.

## 021 Release Hardening / Production Readiness

The 021 phase begins after the 020 module build sprint and focuses on release hardening, route/production readiness, operational smoke testing, role-based production readiness runbooks, and final release-candidate validation.

<!-- MODULE_RECOVERY_CHECKPOINT_20260718_START -->
## July 18, 2026 Module Recovery and Controlled Deployment Checkpoint

| Module | Capability | Status |
|---|---|---|
| 024 | Sales Intake | Recovered, validated, merged, and deployed |
| 025 | SOW Generator | Recovered, validated, merged, and deployed |
| 026 | CRM Integration Framework | Recovered, validated, merged, and deployed |
| 027 | Signed Sales-to-Delivery Handoff | Recovered, validated, merged, and deployed |
| 028 | SOW-Aware AI Time Entry Generator | Recovered, validated, merged, and deployed |
| 029 | UAT Role and Workflow Validation Center | Recovered, validated, merged, and deployed |
| 030 | Reporting, Accounting, Invoicing, and Analytics | Recovered, validated, merged, and deployed |
| 058 | CI/CD Pipeline Center | Recovered, validated, merged, and deployed |
| 059 | Global Session Intelligence | Restored globally, validated, merged, and deployed |
| 999 | Complete User Guide and Help Assistant | Restored, validated in the public bundle, merged, and deployed |

### Recovery evidence

- Recovery checkpoint: `92c0964afdc26dede72e09bf2c8d7c0629126bc0`
- Controlled deployment workflow run: `29664610372`
- Deployed web revision: `ca-phd-test-web-westus3--ciweb-29664610372`
- Public asset: `index-COUkd92V.js`
- Module 999 route and title confirmed in the live bundle
- Module 059 confirmed globally mounted
- Database schema changed: No
- ProjectPulse SSO changed: No
- Workflow rollback required: No

### Module 062 current work

- Integration baseline: `92c0964afdc26dede72e09bf2c8d7c0629126bc0`
- Integration branch: `feature/module-062-unified-identity-profile-20260719T001319Z`
- Status: Module 062 source candidate complete; Module 057, Module 059, and Profile consume the shared identity and presence layer; pending PR review
- Module 057 presence text normalization corrected
- Module 002 remains paused and untouched
- Azure changed: No
- Entra changed: No
- Database changed in Phase 1: No
<!-- MODULE_RECOVERY_CHECKPOINT_20260718_END -->

### Module 997 — Security Operations, Threat Intelligence & Response Center

Module 997 is isolated on
`feature/module-997-security-operations-response-20260720` from verified
`origin/main@3d9a3dca8af479c854dc4c4a9294bc8aad273074`, which contains required
checkpoint `48421d5ba1584d64fc3bd043304c003eff1dc27b`.

Tracker v1.8 coverage: `GOV-017`, `RBAC-021`, `RBAC-022`, `INT-013`, `AI-021`,
`RPT-014`, `OPS-006`, `OPS-017`, `OPS-021`, `OPS-022`, `OPS-023`, `OPS-024`,
`OPS-025`, `OPS-026`, `OPS-027`, and `DATA-012`.

| Capability | Complete source checkpoint | Locked production boundary |
|---|---|---|
| Security overview | Actual session, authorization, severity, domains, ownership, and explicit unknown/delegated states | No live security-health assertion |
| Alerts and incidents | Required schemas, severity, objectives, lifecycle, and empty non-authoritative inventories | No telemetry connector or durable incident store |
| Threat intelligence | Source, confidence, freshness, expiry, handling, and minimization policy | No threat feed, indicator import, or automated block |
| Control posture | Delegated control-owner and evidence map | No live effectiveness claim |
| Response | Detect → triage → declare → contain → eradicate → recover → review → close | All action endpoints HTTP 423 before body read |
| Reporting | Restricted audiences, sanitized fields, prohibited content, and decision evidence | No external notification or evidence export |
| Integrations | Explicit future telemetry, threat, endpoint, network, identity, case, AI, and mail adapters | Every adapter not configured or unauthorized |
| Module 998 handoff | Future controlled-remediation ownership documented | No import, call, execution, or dependency on draft PR 26 |

`MODULE_997_STATUS=SOURCE_VALIDATED_REMOTE_PUBLICATION_PENDING_FAIL_CLOSED`

Validation evidence: Module 997 validator 91/91 passed; the full protected
frontend chain and Vite production build passed with 183 transformed modules;
Module 056E passed; .NET 10.0.302 baseline and candidate builds each completed
with 0 errors and 10 existing warnings, warning delta 0, and Module 997 warnings
0; source diff check and the exact 22-file manifest gate passed. The committed
manifest SHA-256 is `a89fdab5ab16e2c5a031f0b5f53c2296fcbd6c88165daf279a798815022a1b11`.

Commit, push, and draft PR are authorized. Merge, deployment, Azure, database,
Entra, Cloudflare, SMTP, containment, production response, telemetry, threat
feeds, external notification, AI execution, evidence export, rollback, and
secret access remain unauthorized and unchanged.
