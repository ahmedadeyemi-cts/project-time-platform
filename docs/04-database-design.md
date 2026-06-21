# Database Design

## 1. Purpose

This document defines the initial database entities required for the platform. The database design will evolve through versioned migrations.

## 2. Database Platform

The target database is PostgreSQL.

## 3. Initial Entity Groups

### Identity and Access

- users
- roles
- permissions
- user_roles
- role_permissions
- user_scopes

### Organization Relationships

- manager_assignments
- team_lead_assignments
- departments
- cost_centers

### Project Management

- projects
- project_tasks
- project_managers
- project_engineer_assignments
- project_task_assignments
- project_billing_rules

### Time Entry and Approval

- time_periods
- timesheets
- time_entries
- manager_approval_actions
- project_approval_actions

### Accounting Reconciliation

- billing_periods
- reconciliation_batches
- reconciliation_actions
- period_locks

### Utilization

- utilization_rules
- utilization_targets
- utilization_snapshots

### Notifications

- notification_preferences
- notification_queue
- notification_history

### Audit

- audit_log

## 4. Time Entry Status Model

The system must support these statuses:

| Status | Meaning |
|---|---|
| Draft | Engineer has not submitted time |
| Submitted | Engineer submitted time for manager review |
| Manager Declined | Manager rejected time |
| Manager Approved | Manager approved time |
| PM Review Pending | Waiting for Project Manager approval |
| PM Declined | Project Manager rejected project/task allocation |
| PM Approved | Project/task time approved |
| Accounting Review Pending | Ready for accounting reconciliation |
| Reconciled | Accounting reconciled time |
| Locked | Period closed |

## 5. Historical Accuracy

Historical reports must remain accurate even if:

- An engineer changes manager.
- An engineer changes team.
- A project manager changes.
- A project code changes.
- A project task changes.
- Utilization rules change.

This means relationship tables must include effective dates where appropriate.

## 6. Migration Strategy

All database changes must be stored in:

```text
database/migrations/
database/rollback/
```

No manual production database change should be performed unless it is captured in a migration file.

## 7. Initial Migration Plan

Planned first migrations:

1. Create identity and role tables.
2. Create organization assignment tables.
3. Create project and task tables.
4. Create time entry and timesheet tables.
5. Create approval action tables.
6. Create accounting reconciliation tables.
7. Create utilization and notification tables.
8. Create audit log table.
