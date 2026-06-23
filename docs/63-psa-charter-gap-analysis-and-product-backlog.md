# PSA Charter Gap Analysis and Product Backlog

## Source Document

- `USS PSA Project Charter v1.0.docx`
- Version: 1.0
- Date: June 11, 2026
- Sponsor: Darren Olson
- Owner / PM: Matthew LeNoble
- Status: Draft — Pending Approval

## Purpose

This document captures the missing Project Pulse / PSA platform scope identified from the USS PSA Project Charter and translates it into a product backlog for implementation.

The current build has focused heavily on time entry, day-level submission, manager approval, approval locking, and utilization foundations. The charter expands the target solution into a broader Professional Services Automation platform covering the full professional services lifecycle.

## Charter Alignment Summary

The charter defines the platform as the **US Signal Professional Services Automation Platform** with a target go-live of **December 2026**, an initial user count of approximately **50 users**, and a hosting direction of **Microsoft Azure**.

The charter scope includes:

- Project Intake
- Project Management
- Resource Scheduling
- Time Tracking
- Expense Management
- Client Invoicing
- Reporting and dashboards
- Azure hosting
- Microsoft Entra ID / OIDC authentication
- Outlook calendar sync through Microsoft Graph
- Salesforce opportunity handoff
- Emburse expense import

## Current Build Coverage

The current Project Pulse build already covers or partially covers:

- Browser-based internal platform shell
- PostgreSQL-backed application data
- US Signal branding
- Engineer daily time entry
- Normal and afterhours time
- Non-project time categories
- Work location group and location fields
- Autosave on modal close
- Day-level submission requiring at least 8.00 hours
- Submitted-day locking
- Engineer self-unlock window concept
- Manager approval queue
- Manager approval / decline / unlock actions
- Approval notification banner foundation
- Bulk manager approval foundation
- Utilization target foundation
- Audit log foundation

## Missing Scope from Charter

### 1. Authentication and User Management

Missing or incomplete:

- Microsoft Entra ID OIDC authentication for US Signal staff
- Local credential authentication for contractors
- Role-based route guards
- Admin user management module
- Invite / activate / deactivate user workflow
- Initial bulk user onboarding for approximately 50 users

Recommended roles to align with current and charter scope:

- Admin
- Engineer / Staff
- Contractor
- Manager
- Project Manager
- Practice Manager
- Finance
- Accounting
- Executive / Read-only Leadership

### 2. Project Intake and Templates

Missing:

- Client management CRUD
- Unique client code field
- Project creation workflow
- Project auto-code generation: `[CLIENT.code]-[YYYY]-[NNN]`
- Salesforce opportunity ID capture
- CRM source fields on project creation form
- Project template engine
- Template versioning with parent-template history
- Template phases, milestones, tasks, and checklist items
- Project kickoff checklist copied from template
- Contract document management
- Contract document lifecycle for SOW, MSA, NDA, amendment, and change order addendum
- Azure Blob Storage document upload integration

### 3. Project Management Module

Missing:

- Project phase management
- Milestone management
- Task management with subtasks
- Task dependency engine
- Gantt chart visualization using frappe-gantt or equivalent
- Risk register
- Issue tracker
- Weekly status report
- RAG status by project dimension
- Change order workflow
- Practice Manager approval for change orders
- Budget and end-date update after approved change orders

### 4. Resource Scheduling

Missing:

- Resource assignment form
- Assignment to project, phase, and task
- Daily-hours and total-allocated-hours assignment options
- Team availability grid
- Weekly capacity vs. assigned hours view
- Microsoft Graph API calendar push sync
- Outlook event ID tracking
- Outlook sync retry and failure logging
- Calendar update/delete handling

### 5. Time Management Enhancements

Partially covered but still missing:

- Project-task time entries tied to assigned tasks
- PM approval workflow after manager approval
- Approved / rejected status alignment for PM workflow
- Per-project billing rate override lookup
- ProjectRate effective date handling
- Final lock after invoice generation or accounting close
- More robust manager/team routing once Entra identity is connected

### 6. Expense Management

Missing:

- Manual expense entry
- Expense category management
- Expense amount, currency, billable flag
- Receipt upload
- Receipt storage in Azure Blob Storage
- Emburse CSV import
- Configurable CSV field mapping
- Row-level import error log
- Emburse import tracking entity
- Expense approval workflow
- Expense inclusion in invoicing

### 7. Client Invoicing

Missing:

- Invoice generation from approved time and approved expenses
- Invoice date-range selection
- Grouping by project, task, and expense category
- Invoice line item generation
- Hour x rate calculation
- Expense amount line-item calculation
- Tax and subtotal calculation
- Editable draft invoice before send
- Invoice lifecycle: Draft → Sent → Paid
- Voided fallback state
- Invoice lock on time and expense records after billing

### 8. Reporting and Dashboards

Partially covered but still missing:

- Project health dashboard
- RAG summary dashboard
- Project financial dashboard
- Budget vs. actual
- Burn rate
- Outstanding invoices
- PM dashboard
- Active projects widget
- Overdue tasks widget
- Pending approvals widget
- Resource utilization export to CSV
- Leadership rollup view

### 9. Infrastructure and DevOps

Current build is running on OCI/Rocky Linux for development, but the charter target is Microsoft Azure.

Missing for charter alignment:

- Azure App Service hosting plan
- Azure Database for PostgreSQL Flexible Server
- Azure Blob Storage
- Azure Key Vault
- Azure Application Insights
- Azure CDN / egress planning if needed
- Azure backup validation
- GitHub Actions deployment pipeline to Azure App Service
- Staging and production deployment gates
- Environment variable and secret management
- TLS/DNS production validation

### 10. UAT, Training, and Go-Live

Missing:

- UAT test plan
- Role-based UAT test scripts
- Defect log process
- P0/P1 defect criteria
- Training plan by role
- Quick reference guides
- Production cutover checklist
- Hypercare plan for the first 30 days after launch
- Legacy tracking decommission plan

## Recommended Product Backlog

### Epic 1: Identity, Roles, and User Administration

Priority: High

Deliverables:

- Entra ID OIDC login
- Contractor local login
- Role-based route guards
- User CRUD
- User invite workflow
- User activation/deactivation
- Manager and PM relationship mapping

Acceptance criteria:

- US Signal staff can sign in with Entra ID.
- Contractors can sign in with local credentials.
- Managers only see approvals assigned to them.
- PMs only see project approval items for their projects.

### Epic 2: Project Intake and Template Engine

Priority: High

Deliverables:

- Client CRUD
- Project CRUD
- Auto-generated project code
- Salesforce opportunity ID field
- CRM handoff metadata
- Template CRUD
- Template versioning
- Kickoff checklist
- Contract document upload

Acceptance criteria:

- A PM can create a project from a template.
- Project phases, tasks, milestones, and checklist items are copied from the selected template.
- Contract documents can be attached and tracked by status.

### Epic 3: Project Task Assignment and Time Entry Completion

Priority: High

Deliverables:

- Project task assignments to engineers
- Open tasks dropdown in timesheet
- Regular tasks dropdown
- Service request placeholder workflow
- Manager approval routing
- PM approval after manager approval
- Approved-entry locking

Acceptance criteria:

- Engineers can only enter project time against assigned project tasks.
- Manager-approved time moves to PM approval.
- PM-approved time becomes eligible for invoicing.
- Engineers cannot edit approved time.

### Epic 4: Approval Inbox

Priority: High

Deliverables:

- Dedicated approval page/workspace
- Notification badge on login
- Manager approval queue
- PM approval queue
- Bulk manager approval
- Bulk PM approval
- Decline with required reason
- Approval filters by engineer, project, customer, date, and status

Acceptance criteria:

- A manager can approve multiple submitted days at once.
- A PM can approve multiple project entries at once.
- Declined items return to the engineer with a visible reason.
- Approved items cannot be edited by the engineer.

### Epic 5: Expense Management and Emburse Import

Priority: Medium

Deliverables:

- Manual expense entry
- Receipt upload
- Azure Blob receipt storage
- Emburse CSV import
- Field mapping
- Import error log
- PM expense approval

Acceptance criteria:

- Expenses can be entered manually or imported from Emburse CSV.
- Receipts are stored securely.
- Approved expenses become eligible for invoicing.

### Epic 6: Resource Scheduling and Outlook Sync

Priority: Medium

Deliverables:

- Resource assignment screen
- Capacity and availability grid
- Outlook calendar sync
- Graph API retry/backoff
- Sync status tracking

Acceptance criteria:

- PMs can assign resources to projects, phases, and tasks.
- Assignments appear in Outlook calendars.
- Failed sync attempts are logged and retryable.

### Epic 7: Project Management Module

Priority: Medium

Deliverables:

- Phases
- Milestones
- Tasks and subtasks
- Dependencies
- Gantt view
- Risk register
- Issue tracker
- Weekly RAG reports
- Change order workflow

Acceptance criteria:

- PMs can manage project execution from intake through closeout.
- Risks, issues, RAG status, and change orders are tracked in the platform.

### Epic 8: Invoicing and Reporting

Priority: Medium

Deliverables:

- Invoice generation
- Invoice line items
- Rate override lookup
- Expense inclusion
- Invoice lifecycle
- Invoice record locking
- Financial dashboard
- Utilization CSV export

Acceptance criteria:

- Finance can generate a draft invoice from approved time and expenses.
- Invoice generation takes under two hours per cycle.
- Time and expense records included on invoices are locked.

### Epic 9: Azure Production Readiness

Priority: High before production

Deliverables:

- Azure infrastructure buildout
- Key Vault secret storage
- App Insights logging
- GitHub Actions CI/CD
- Staging and production environments
- Backup and recovery validation

Acceptance criteria:

- Production runs in Azure.
- Secrets are not stored in repo or flat files.
- Staging deployments are validated before production promotion.

### Epic 10: UAT, Training, and Go-Live

Priority: High before launch

Deliverables:

- UAT plan
- Role-based test scripts
- Defect triage workflow
- Training sessions
- Quick reference guides
- Go-live checklist
- 30-day hypercare plan

Acceptance criteria:

- Representative users from each role complete UAT.
- P0/P1 issues are resolved before go-live.
- All initial users are onboarded within 30 days of go-live.

## Recommended Immediate Next Build Steps

1. Stabilize engineer save/lock/unlock behavior after manager approval.
2. Create a dedicated Approval Inbox route instead of showing manager approval at the bottom of the main page.
3. Complete bulk manager approval validation.
4. Add PM approval stage after manager approval.
5. Add project/client/task seed data and assign engineers to project tasks.
6. Connect project-task entries to the timesheet Open Tasks dropdown.
7. Add manager-decline correction visibility on the engineer timesheet.
8. Begin user/role model preparation for Entra ID.

## Notes on Naming

The charter uses the name **US Signal Professional Services Automation Platform** and code **USS-PSA-2026**. The current application branding uses **Project Pulse**. These can coexist if Project Pulse is treated as the internal product name for the PSA platform.

Recommended product naming:

- Formal project name: US Signal Professional Services Automation Platform
- Internal product/application name: Project Pulse
- Project code: USS-PSA-2026

## Status

Backlog captured and ready for prioritization.
