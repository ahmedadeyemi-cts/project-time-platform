# Running Implementation Document

## Project Time, Utilization, Approval, Project Management, and Accounting Reconciliation Platform

Version: 0.1  
Owner: Ahmed Adeyemi  
Repository: `ahmedadeyemi-cts/project-time-platform`  
Status: Foundation setup

## 1. Purpose

This document is the living implementation record for the platform. It will be updated as the system is designed, built, deployed, tested, upgraded, and maintained.

The purpose of this document is to make the process reproducible. Any future engineer, administrator, or vendor should be able to understand what was built, why it was built, how it was deployed, how it is tested, and how it is upgraded safely.

## 2. Core Business Goal

Build a self-hosted platform that allows engineers to enter time against assigned projects and tasks, managers to approve or decline engineer time, project managers to approve project/task time for billing readiness, accounting to reconcile approved time, and leadership to view utilization and operational reporting.

## 3. Cost and Hosting Direction

The platform will avoid new paid subscriptions where possible. The current target approach is:

| Area | Decision |
|---|---|
| Source Control | GitHub |
| Authentication | Existing Microsoft Entra ID |
| Test Hosting | Oracle Free Tier, if suitable |
| End-State OS | Rocky Linux |
| Database | PostgreSQL |
| Backend | .NET / ASP.NET Core |
| Frontend | React |
| Containers | Podman |
| Reverse Proxy | Caddy or NGINX |

## 4. Reproducibility Rule

Every major step must be documented in the repository.

This includes:

- Installation steps
- Configuration changes
- Database migrations
- Deployment steps
- Upgrade steps
- Backup and restore steps
- Testing procedures
- Known issues
- Rollback instructions

No production-like change should be considered complete until the documentation has been updated.

## 5. Current Confirmed Requirements

The system must support:

- Microsoft Entra ID authentication
- Engineer time entry
- Engineer-to-manager assignment
- Engineer-to-team-lead assignment
- Project Manager role
- Project and task management
- Engineer assignment to projects/tasks
- Manager approval and decline workflow
- Project Manager approval and decline workflow
- Accounting reconciliation
- Organizational Admin role
- Team Lead visibility without approval authority
- Monthly utilization emails
- Quarterly utilization emails
- 70% utilization target calculation
- Next 5% utilization increment calculation
- Notification preferences
- Historical reporting
- Audit logging
- Upgrade-safe operations

## 6. Role Summary

| Role | Purpose |
|---|---|
| Engineer | Enter time and view own utilization |
| Team Lead | View assigned team members without approval authority |
| Manager | Approve or decline assigned engineer time |
| Project Manager | Manage projects/tasks and approve project/task time |
| Accounting | Reconcile approved time and manage month-end close |
| Organizational Admin | View/manage organization-wide operational data |
| System Admin | Configure system, identity, roles, and platform settings |
| Super Admin | Emergency full access with audit trail |

## 7. Current Workflow Target

1. Engineer enters time against an assigned project/task.
2. Engineer submits timesheet.
3. Manager approves or declines engineer time.
4. If declined, engineer receives a notification and corrects the entry.
5. If manager-approved, Project Manager reviews project/task allocation.
6. Project Manager approves or declines project/task time.
7. If PM-approved, time becomes ready for accounting.
8. Accounting reconciles time for month-end billing.
9. Accounting locks the period.
10. Historical records remain available for reports and audit.

## 8. Implementation Phases

### Phase 1: Foundation

- Create repository structure.
- Create documentation set.
- Select and document target stack.
- Create initial architecture.
- Create initial database design.
- Prepare Rocky Linux setup runbook.

### Phase 2: Authentication and Security

- Register Microsoft Entra application.
- Implement OIDC login.
- Create user and role tables.
- Implement RBAC.
- Implement audit logging.

### Phase 3: Project and Assignment Management

- Create projects.
- Create tasks.
- Assign engineers to projects/tasks.
- Assign managers, team leads, and project managers.

### Phase 4: Time Entry and Approval

- Build engineer time entry.
- Build manager approval queue.
- Build PM approval queue.
- Build accounting reconciliation queue.

### Phase 5: Reporting and Notifications

- Build dashboards.
- Build utilization calculation.
- Build monthly and quarterly utilization notifications.
- Build manager and organization-level reports.

### Phase 6: Deployment and Operational Hardening

- Containerize services.
- Create deployment scripts.
- Add backups.
- Add upgrade testing.
- Add regression tests.
- Add monitoring.

## 9. Open Decisions

| Decision | Status |
|---|---|
| Final platform name | Open |
| Caddy vs NGINX | Open |
| React vs Angular | React currently preferred |
| PostgreSQL version | Open |
| Rocky Linux version | Open |
| Utilization based on approved or reconciled hours | Open |
| PM approval sequence after manager approval or parallel | Manager first currently preferred |
| Email method: Graph, SMTP relay, or other | Open |
| Reporting: built-in only or Superset later | Built-in first |

## 10. Change Log

| Date | Change |
|---|---|
| Initial | Repository and documentation foundation created |
