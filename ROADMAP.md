# Project Health Dashboard PSA Production Roadmap

This roadmap tracks the remaining modules needed to mature Project Health Dashboard into a robust ChangePoint replacement. It reflects the current implementation direction and the expanded requirements around time compliance, reporting, Emburse Certify, project delivery, accounting, help, production hardening, staging, test-data reset controls, Azure migration, long-term capacity planning, and future multi-server readiness.

## Current Foundation

The platform already has a strong foundation in the following areas:

- User Administration
- Azure / Entra Admin
- Role Admin
- Local admin password reset approval
- Forced local admin password change
- Audit History
- Timesheet
- Approvals
- Utilization
- Holidays
- Service Control Center
- Version inventory / API status / restart history
- Backup / DR Center
- Restore Validation Center with selectable restore point
- Backup Retention Center
- Replication / Sync Status Center
- Sidebar reclassification into enterprise module groups

## Target Module Structure

### Pinned

- Dashboard

### Work Management

- Timesheet
- Approvals
- Utilization
- Holidays
- Time Compliance

### Project Operations

- Project Intake
- Clients
- Project Templates
- Project Workspace
- Project Documents
- Change Orders

### Project Management

- Phases & Milestones
- Tasks & Gantt
- Risks & Issues
- Weekly RAG Status

### Resource Management

- Resource Scheduling
- Team Availability
- Engineering Resource Requests
- Outlook Calendar Sync

### Expense & Accounting

- Expenses
- Emburse Certify
- Receipt Management
- Expense Approvals
- Invoicing
- Accounting Review
- Billing Rates

### Reporting & Accountability

- Executive Dashboard
- Engineer Reports
- PM Reports
- Project Financials
- Expense Reports
- Utilization Reports
- Export Center

### Admin & Identity

- User Admin
- Azure / Entra Admin
- Role Admin

### Security & Audit

- Audit History

### Platform Operations

- Services
- Notification Center
- Help Center
- Staging & Deployment
- Test Data Reset
- Azure Environment Readiness
- Capacity Planning

### Resilience & Recovery

- Backup / DR
- Restore Validation
- Backup Retention
- Replication / Sync

## Remaining Roadmap

### 019M-O — Time Compliance & Notification Center

Goal: automate weekly time-entry reminders, month-end invoicing reminders, holiday reminders, and escalation notifications.

Scope:

- Weekly reminder default: Monday 6:00 AM Central
- Weekly escalation default: Monday 8:00 AM Central
- Identify engineers who have not submitted required weekly time
- Send reminder to engineer and always copy manager and Project Team Coordinator
- Send escalation if time is still missing by the escalation deadline
- Month-end reminder configuration page
- Month-end deadline rule: last Monday, Tuesday, Wednesday, Thursday, Friday, or custom date override
- Holiday reminders based on uploaded holiday schedule
- Send holiday reminders 7 days before and 1 day before weekday holidays
- Notification templates
- Notification send history
- Retry prevention / duplicate-send protection
- Test-send controls
- Audit trail for notification configuration and sends

### 019M-P — Help Center Foundation

Goal: turn the Help button into a searchable internal platform support center.

Scope:

- Searchable FAQ
- Module-by-module help articles
- How-to guidance for engineers, PMs, managers, coordinators, accounting, and admins
- Safe operational runbooks
- Future Claude Enterprise integration
- Curated documentation index
- Security boundary preventing secrets, credentials, environment files, and unrestricted filesystem data from being exposed

### 019M-Q — Project Intake Foundation

Goal: create the official entry point for projects.

Scope:

- Client management
- Project creation workflow
- Auto project code generator
- Salesforce opportunity ID and CRM source fields
- Project template engine
- Template versioning
- Kickoff checklist
- Contract/SOW/MSA/NDA/change order document tracking

### 019M-R — Project Management Workspace

Goal: provide a PM workspace for project execution and delivery control.

Scope:

- Project workspace
- Phases and milestones
- Tasks/subtasks
- Dependency engine
- Gantt view
- Risk register
- Issue tracker
- Weekly RAG status reports
- Change order workflow

### 019M-S — Resource Management & Engineering Resource Requests

Goal: manage resource assignments and let engineers request support from other engineers.

Scope:

- Resource Scheduling
- Team Availability grid
- Engineering Resource Request page
- Engineer requests help from another engineer or skillset
- Route request to assigned PM, requesting engineer manager, and Project Team Coordinator
- Optional future route to requested engineer manager or practice manager
- Request statuses: Draft, Submitted, PM Review, Manager Review, Coordinator Review, Approved, Rejected, Cancelled, Fulfilled

### 019M-T — Expense Management & Emburse Certify

Goal: support project-linked expenses and Emburse Certify integration.

Scope:

- Expense entry
- Receipt upload
- Expense categories
- Billable / non-billable expense flag
- Project-linked expenses
- Emburse Certify integration foundation
- CSV import and field mapping
- Row-level import errors
- PM expense approval workflow
- Expense audit trail

### 019M-U — Invoicing & Accounting Review

Goal: generate invoices from approved time and expenses and provide accounting controls.

Scope:

- Approved time to invoice
- Approved expenses to invoice
- Invoice line items
- Draft / Sent / Paid / Voided invoice lifecycle
- Billing rate overrides
- Accounting review queue
- Billing holds
- Budget tracking
- Burn rate
- Outstanding invoices
- Billing discrepancy controls

### 019M-V — Reporting & Accountability Center

Goal: provide sophisticated reports for engineers, PMs, accounting, utilization, expenses, and executives.

Scope:

- Engineer accountability reports
- Missing / late time reports
- Billable vs non-billable reports
- PM approval aging reports
- Projects missing RAG reports
- Project financials
- Approved but unbilled time / expense reports
- Expenses by project and engineer
- Utilization reports
- Executive dashboard
- Export Center

### 019M-W — Salesforce CRM Handoff

Goal: support Salesforce opportunity handoff into Project Health Dashboard.

Scope:

- Salesforce opportunity ID on project intake
- CRM source fields
- Manual v1 handoff
- Future API-read foundation if approved
- No bidirectional Salesforce sync in v1 unless scope changes

### 019M-X — Outlook Calendar Sync

Goal: push resource/project assignments to Microsoft Outlook calendars.

Scope:

- Microsoft Graph configuration
- Calendar push for resource assignments
- Store outlookEventId and outlookSyncedAt
- Update/delete handling
- Retry/backoff
- Sync failure logging

### 019M-Y — Project Health Dashboard Charter v2.0 Documentation Update

Goal: update the PSA charter to reflect the actual implementation stack and expanded production roadmap.

Scope:

- Update stack from the original proposed stack to the current Project Health Dashboard implementation direction
- Document .NET API, React/Vite frontend, PostgreSQL, Linux/systemd operations, internal auth/session model, backup/DR, restore validation, replication readiness, Help Center, Claude Enterprise future integration, Enterprise GitHub migration, notification center, reporting/accountability, staging, test data reset, Azure migration, long-term capacity planning, and production hardening

### 019M-Z — Production Security Hardening & Enterprise GitHub Readiness

Goal: prepare Project Health Dashboard for production and future enterprise GitHub ownership.

Scope:

- Branch protection
- Pull request review requirements
- Secret scanning
- Dependency scanning
- CI/CD validation
- Backup before deploy
- Deployment approval gates
- RBAC review
- Audit coverage review
- Security headers
- Rate limiting
- TLS/certificate validation
- Least-privilege service accounts
- Operational runbooks

### 019M-AA — Staging Environment & Deployment Pipeline

Goal: create a safer deployment model before production.

Scope:

- Staging server build plan
- Separate staging database
- Separate staging frontend/API services
- Separate staging DNS name
- Deployment script for staging
- Promote-to-production process
- Pre-deploy backup
- Post-deploy health checks
- Restore validation after deploy
- Rollback plan
- GitHub Actions staging workflow

### 019M-AB — Controlled Test Data Reset

Goal: provide a one-time and restricted way to wipe test data during the build/UAT period without damaging production data.

Scope:

- Staging/test-only reset controls
- Explicit environment guard to block production execution
- Required admin confirmation phrase
- Required pre-reset backup
- Reset preview/dry-run
- Reset selected business data while preserving admin users, roles, permissions, configuration, audit baseline, and system settings
- Root-owned reset runner
- Reset audit log
- Reset history page
- Final disable/remove option before production go-live

### 019M-AC — Azure Migration & Production Architecture

Goal: migrate from the current lab/server build into an Azure-aligned staging and production platform.

Scope:

- Azure staging environment
- Azure production environment
- Azure PostgreSQL architecture decision
- Azure App Service, VM, or container hosting decision
- Azure Blob Storage for documents, receipts, backups, and exports
- Azure Key Vault for secrets
- Azure Monitor / Application Insights logging strategy
- Private networking and firewall rules
- TLS/DNS production plan
- Environment separation for dev, staging, and production
- Data migration plan from the current build into Azure staging
- Production cutover plan after staging validation

### 019M-AD — Load Testing & Long-Term Capacity Planning

Goal: define compute, database, storage, and scaling requirements for a platform expected to run for 10+ years.

Scope:

- Load testing plan
- Baseline performance tests
- Peak usage simulation
- Notification burst testing
- Report generation load testing
- File upload/download testing
- Database growth forecast
- Storage growth forecast for receipts, documents, exports, logs, and backups
- Compute sizing recommendation for staging and production
- Horizontal and vertical scaling strategy
- Database indexing and query performance review
- Retention policies for logs, backups, reports, and notification history
- Capacity review schedule after go-live

## Automation Strategy

Project Health Dashboard should use automation to reduce repetitive manual work and support around-the-clock progress.

Recommended automation layers:

1. GitHub Actions CI
   - Build .NET API
   - Build React/Vite frontend
   - Run tests
   - Run dependency scan
   - Run secret scan
   - Package frontend and backend artifacts

2. GitHub Actions deployment workflow
   - Deploy to staging on feature branch or main merge
   - Require approval before production
   - Run backup before deploy
   - Restart services safely
   - Run health checks after deploy

3. Background systemd jobs
   - Time compliance reminder scheduler
   - Month-end reminder scheduler
   - Holiday reminder scheduler
   - Backup scheduler
   - Restore validation scheduler
   - Replication readiness exporter

4. Work queue runners
   - Notification send queue
   - Backup queue
   - Backup delete queue
   - Test data reset queue
   - Future import/export queues
   - Future Salesforce/Emburse/Outlook sync queues

5. Documentation automation
   - Generate module inventory
   - Generate endpoint inventory
   - Generate role/permission matrix
   - Feed safe docs into Help Center and future Claude Enterprise assistant

## Security Principles

- RBAC on every API endpoint
- All sensitive actions audited
- No secrets shown in UI
- No arbitrary file paths accepted from the UI
- All background jobs run under least privilege
- All notification recipients resolved from trusted system records
- Duplicate notification protection
- Integration settings admin-only
- Claude/AI help only receives curated documentation and safe metadata
- Enterprise GitHub migration before production
- No test-data reset control may run in production
- Staging must be validated before production deployment
- Production cutover must be based on validated staging results
- Compute sizing must be validated by load testing before go-live
