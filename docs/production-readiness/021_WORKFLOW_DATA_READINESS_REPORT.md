# 021 Workflow Data Readiness Report

Generated UTC: `2026-06-30T16:42:26.434012+00:00`

## Purpose

This report validates whether each production-critical workflow has backend endpoint signals, route visibility signals, and database table references that can support release-candidate validation.

## Validation Model

- `ready_for_live_validation`: expected endpoints are present, table references exist, and related routes are visible in the route inventory.
- `needs_data_confirmation`: route and endpoint signals exist, but live database counts should be confirmed.
- `needs_review`: route or endpoint mapping needs additional review before release-candidate validation.

## Workflow Readiness Matrix

| Workflow Area | Status | Endpoints Present | Table Signals | Route Signals |
|---|---|---:|---:|---:|
| Customer Directory | `ready_for_live_validation` | 2/2 | 1 | 3 |
| Project Intake | `needs_data_confirmation` | 5/6 | 1 | 23 |
| Resource Assignment | `ready_for_live_validation` | 4/4 | 1 | 11 |
| Approval Workflow | `ready_for_live_validation` | 5/5 | 1 | 27 |
| Export Package | `ready_for_live_validation` | 4/4 | 2 | 19 |
| Audit Evidence | `ready_for_live_validation` | 3/3 | 2 | 20 |
| Production Readiness Command Center | `ready_for_live_validation` | 3/3 | 5 | 36 |

## Customer Directory

Customer, contact, and account context is available before project intake and downstream workflow activity.

### Readiness Checks

- At least one active customer exists.
- Customer records have usable names and ownership context.
- Customer contact data is available where required by intake workflows.

### Endpoint Signals

| Endpoint | Present |
|---|---|
| `/api/customers/overview` | Yes |
| `/api/customers` | Yes |

### Table Signals

| Table | Static Mentions |
|---|---:|
| `customers` | 9 |
| `customer_contacts` | 0 |
| `customer_locations` | 0 |

### Route Signals

| Route | Title | Group | Status |
|---|---|---|---|
| `#customer-directory` | Customer Directory | Ungrouped | Missing |
| `#project-intake` | Project Intake | Project Intake | Missing |
| `#customer-directory` | Customer Directory | Customers | Missing |

## Project Intake

Intake records can be reviewed, linked, promoted, and prepared for project execution.

### Readiness Checks

- Open and recently completed intake records are available for review.
- Intake records can be associated with projects or project-link candidates.
- Supporting documents and handoff details are visible where applicable.

### Endpoint Signals

| Endpoint | Present |
|---|---|
| `/api/project-intake/summary` | Yes |
| `/api/project-intake/overview` | No |
| `/api/project-intake/work-task-handoff` | Yes |
| `/api/project-intake/project-link-options` | Yes |
| `/api/project-intake/resource-assignment-handoff` | Yes |
| `/api/project-intake/resource-assignment-promotions` | Yes |

### Table Signals

| Table | Static Mentions |
|---|---:|
| `project_intake_requests` | 15 |
| `project_intakes` | 0 |
| `project_intake_supporting_documents` | 0 |
| `project_intake_work_tasks` | 0 |

### Route Signals

| Route | Title | Group | Status |
|---|---|---|---|
| `#project-workload` | Project Workload | Ungrouped | Missing |
| `#project-workspace` | Project Workspace & Engineering Documents | Ungrouped | Missing |
| `#project-intake` | Project Intake & Engineering Resource Requests | Ungrouped | Missing |
| `#customer-directory` | Customer Directory | Ungrouped | Missing |
| `#cost-alerts` | Cost Overrun Alerts | Ungrouped | Missing |
| `#time-compliance` | Time Compliance & Notification Center | Ungrouped | Missing |
| `#timesheet` | Time Entry | Ungrouped | Missing |
| `#project-allocation-info` | Project Allocation and Info | Ungrouped | Missing |
| `#psa-modules` | PSA Modules | Ungrouped | Missing |
| `#workflow` | Workflow | Ungrouped | Missing |
| `#work-task-builder` | Work Task Builder | Ungrouped | Missing |
| `#backup-dr` | Backup / DR Center | Ungrouped | Missing |

## Resource Assignment

Project demand can be matched to available resource and capacity signals.

### Readiness Checks

- Active users include assignable delivery resources.
- Project allocation records can be reviewed.
- Capacity or assignment views provide enough information to support staffing decisions.

### Endpoint Signals

| Endpoint | Present |
|---|---|
| `/api/resource-scheduling/capacity` | Yes |
| `/api/project-allocation-info/source-projects` | Yes |
| `/api/project-allocation-info/engineers` | Yes |
| `/api/project-allocation-info/projects` | Yes |

### Table Signals

| Table | Static Mentions |
|---|---:|
| `resource_assignments` | 0 |
| `project_resource_assignments` | 0 |
| `project_allocations` | 0 |
| `app_users` | 106 |

### Route Signals

| Route | Title | Group | Status |
|---|---|---|---|
| `#project-workspace` | Project Workspace & Engineering Documents | Ungrouped | Missing |
| `#project-intake` | Project Intake & Engineering Resource Requests | Ungrouped | Missing |
| `#utilization` | Utilization | Ungrouped | Missing |
| `#project-allocation-info` | Project Allocation and Info | Ungrouped | Missing |
| `#psa-modules` | PSA Modules | Ungrouped | Missing |
| `#utilization` | Utilization | Resource Management | Missing |
| `#project-intake` | Project Intake | Project Intake | Missing |
| `#cost-alerts` | Cost Alert Overrun | Cost Control | Active |
| `#user-admin` | User Administration | Security | Active |
| `#work-task-builder` | Work Task Builder | Security | Active |
| `#dashboard` | Engineer Negative Access Smoke | Security | Missing |

## Approval Workflow

Submitted work can be reviewed, approved, declined, unlocked, and audited through controlled workflow actions.

### Readiness Checks

- Pending and completed approval records are available for review.
- Approval actions are role-gated.
- Workflow summary data clearly separates pending review, approved, and blocked items.

### Endpoint Signals

| Endpoint | Present |
|---|---|
| `/api/manager/approvals` | Yes |
| `/api/workflow/approval-items` | Yes |
| `/api/workflow/approval-items/action` | Yes |
| `/api/workflow/action-capabilities` | Yes |
| `/api/workflow/approval-export-summary` | Yes |

### Table Signals

| Table | Static Mentions |
|---|---:|
| `manager_approval_requests` | 0 |
| `time_approval_requests` | 0 |
| `time_entries` | 50 |
| `time_workflow_locks` | 0 |

### Route Signals

| Route | Title | Group | Status |
|---|---|---|---|
| `#project-workload` | Project Workload | Ungrouped | Missing |
| `#cost-alerts` | Cost Overrun Alerts | Ungrouped | Missing |
| `#time-compliance` | Time Compliance & Notification Center | Ungrouped | Missing |
| `#manager-approval` | Approval Inbox | Ungrouped | Missing |
| `#utilization` | Utilization | Ungrouped | Missing |
| `#psa-modules` | PSA Modules | Ungrouped | Missing |
| `#workflow` | Workflow | Ungrouped | Missing |
| `#manager-approval` | Approval Inbox | Approvals | Missing |
| `#workflow` | Approval / Export / Audit Workflow | Approvals | Missing |
| `#project-workload` | Project Workload | Project Management | Missing |
| `#customer-directory` | Customer Directory | Customers | Missing |
| `#time-compliance` | Time Compliance | Compliance | Active |

## Export Package

Approved work can move into controlled export and reconciliation readiness.

### Readiness Checks

- Approved or export-ready time exists.
- Export package history or readiness evidence is visible.
- Reconciliation and lock evidence can be reviewed before downstream handoff.

### Endpoint Signals

| Endpoint | Present |
|---|---|
| `/api/time-exports` | Yes |
| `/api/export-packages/readiness-summary` | Yes |
| `/api/workflow/reconciliation-workbench` | Yes |
| `/api/workflow/lock-evidence` | Yes |

### Table Signals

| Table | Static Mentions |
|---|---:|
| `time_workflow_exports` | 13 |
| `time_export_packages` | 0 |
| `time_export_package_items` | 0 |
| `time_entries` | 50 |

### Route Signals

| Route | Title | Group | Status |
|---|---|---|---|
| `#workflow` | Workflow | Ungrouped | Missing |
| `#workflow` | Approval / Export / Audit Workflow | Approvals | Missing |
| `#customer-directory` | Customer Directory | Customers | Missing |
| `#audit-history` | Audit History | Audit | Active |
| `#azure-admin` | Azure / Entra Administration | Security | Missing |
| `#workflow` | Workflow Operational Readiness | Approval / Export / Audit | Missing |
| `#workflow` | Export Packages | Approval / Export / Audit | Missing |
| `#workflow` | Workflow Audit Evidence | Approval / Export / Audit | Missing |
| `#workflow` | Audit History Events | Approval / Export / Audit | Missing |
| `#workflow` | Workflow Preflight Validation | Approval / Export / Audit | Missing |
| `#workflow` | Export Package Readiness Summary | Approval / Export / Audit | Missing |
| `#workflow` | Export Package Evidence Detail | Approval / Export / Audit | Missing |

## Audit Evidence

Security, workflow, approval, export, and administrative actions are traceable.

### Readiness Checks

- Audit history is populated.
- Audit filters support operator, action, and workflow review.
- Export, approval, administrative, and notification activity is traceable.

### Endpoint Signals

| Endpoint | Present |
|---|---|
| `/api/audit/history` | Yes |
| `/api/audit-history/summary` | Yes |
| `/api/audit-history/events` | Yes |

### Table Signals

| Table | Static Mentions |
|---|---:|
| `audit_logs` | 17 |
| `audit_events` | 0 |
| `system_email_provider_test_events` | 2 |

### Route Signals

| Route | Title | Group | Status |
|---|---|---|---|
| `#time-compliance` | Time Compliance & Notification Center | Ungrouped | Missing |
| `#workflow` | Workflow | Ungrouped | Missing |
| `#audit-history` | Audit / Security History | Ungrouped | Missing |
| `#workflow` | Approval / Export / Audit Workflow | Approvals | Missing |
| `#audit-history` | Audit History | Audit | Active |
| `#service-control` | Service Control Center | Operations | Missing |
| `#restore-validation` | Restore Validation Center | Operations | Missing |
| `#workflow` | Workflow Operational Readiness | Approval / Export / Audit | Missing |
| `#workflow` | Export Packages | Approval / Export / Audit | Missing |
| `#workflow` | Workflow Audit Evidence | Approval / Export / Audit | Missing |
| `#workflow` | Audit History Events | Approval / Export / Audit | Missing |
| `#workflow` | Workflow Preflight Validation | Approval / Export / Audit | Missing |

## Production Readiness Command Center

Production readiness status can be reviewed from consolidated operational indicators.

### Readiness Checks

- Production readiness endpoint is available.
- Route registry and module visibility evidence are available.
- Readiness indicators cover users, projects, time, audit, and route contracts.

### Endpoint Signals

| Endpoint | Present |
|---|---|
| `/api/production/readiness-command-center` | Yes |
| `/api/navigation/registry-integrity` | Yes |
| `/api/dashboard/module-visibility-smoke` | Yes |

### Table Signals

| Table | Static Mentions |
|---|---:|
| `dashboard_module_visibility_expectations` | 7 |
| `app_users` | 106 |
| `projects` | 84 |
| `time_entries` | 50 |
| `audit_logs` | 17 |

### Route Signals

| Route | Title | Group | Status |
|---|---|---|---|
| `#project-workload` | Project Workload | Ungrouped | Missing |
| `#project-workspace` | Project Workspace & Engineering Documents | Ungrouped | Missing |
| `#project-intake` | Project Intake & Engineering Resource Requests | Ungrouped | Missing |
| `#customer-directory` | Customer Directory | Ungrouped | Missing |
| `#time-compliance` | Time Compliance & Notification Center | Ungrouped | Missing |
| `#utilization` | Utilization | Ungrouped | Missing |
| `#workflow` | Workflow | Ungrouped | Missing |
| `#restore-validation` | Restore Validation | Ungrouped | Missing |
| `#replication-sync` | Replication & Sync Status | Ungrouped | Missing |
| `#dashboard` | dashboard | Ungrouped | Missing |
| `#dashboard` | dashboard | Ungrouped | Missing |
| `#workflow` | Approval / Export / Audit Workflow | Approvals | Missing |

## Live Database Probe

The generated SQL probe can be run during release-candidate validation:

- `database/reports/021-workflow-data-readiness-probe.sql`

The probe is read-only and reports whether expected workflow tables exist and how many records each table contains.
