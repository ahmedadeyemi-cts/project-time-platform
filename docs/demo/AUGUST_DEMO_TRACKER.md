# ProjectPulse August Demo Tracker

## Completed Demo Foundation

### 019M-O Time Compliance & Notification Center
- Dry-run Time Compliance page created.
- Session-authenticated API calls fixed.
- Route isolation fixed.
- Weekly reminder default shown as Monday 6:00 AM Central.
- Weekly escalation default shown as Monday 8:00 AM Central.
- Holiday reminder windows shown for 7-day and 1-day reminders.
- Manager and Project Team Coordinator copy visibility working.
- Dry-run notification history working.
- Real send intentionally locked.

## Come Back / Production Hardening Items

### Time Compliance
- Replace UI-only month-end selector with persisted settings.
- Add editable reminder templates backed by database.
- Add real notification provider integration only after approval.
- Add notification approval workflow before real send.
- Add notification_log entries in addition to notification_outbox history.
- Add deduplication controls so repeated dry-runs are grouped by run ID.
- Add run batch table for notification preview/send batches.
- Add export of dry-run preview to CSV/PDF.
- Add real timezone scheduling logic for Central Time.
- Add business-day adjustment for holiday reminders that fall on weekends.
- Remove duplicate Ahmed demo identities or mark one inactive after testing.

### Role Enforcement / User Switcher
- Confirm all roles and permissions are database-driven.
- Add admin-safe user switcher for demo mode only.
- Add visible role banner showing current user, role, and permissions.
- Add route guard messaging when a user lacks access.
- Add audit entry when demo user switcher is used.

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
- Keep current demo modules.
- Add clearer readiness badges.
- Add exportable DR readiness summary later.

### Help Center
- Add article index.
- Add per-module help article links.
- Add admin support guidance.

## Items Not in August Demo Scope
- Full production Azure migration.
- Salesforce API integration.
- Outlook calendar sync.
- Multi-server communication.
- Real email sending.

## 019M-P Project Intake + Engineering Resource Request

### Demo Foundation Added
- Project Intake route/page shell.
- Engineering Resource Request route section.
- Demo intake records.
- Demo client/project/task records.
- Demo resource profiles, skills, capacity plans, and assignment readiness.
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

### Demo Foundation Added
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
