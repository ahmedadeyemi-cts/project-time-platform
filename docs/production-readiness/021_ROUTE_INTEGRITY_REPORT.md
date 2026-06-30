# 021 Route Integrity Report

Generated UTC: `2026-06-30T17:18:09.018699+00:00`

## Summary

- Route definitions found: **68**
- Navigation groups found: **17**
- Duplicate routes: **22**
- Duplicate hrefs: **4**
- Missing href: **34**
- Missing title: **3**
- Missing navLabel: **37**
- Missing group: **26**
- Href mismatches: **5**

## Status Counts

- Active: **6**
- Missing: **54**
- Operational: **8**

## Production Operations Route Configs

- `dashboard`
- `workflow`

## Integrity Findings

- Duplicate route keys: audit-history, azure-admin, backup-dr, backup-retention, cost-alerts, customer-directory, dashboard, holiday-admin, manager-approval, project-intake, project-workload, project-workspace, replication-sync, restore-validation, role-admin, service-control, time-compliance, timesheet, user-admin, utilization, work-task-builder, workflow
- Duplicate hrefs: #backup-retention, #dashboard, #production-readiness, #restore-validation
- Href mismatches: 5
- Missing href: 34
- Missing title: 3
- Missing navLabel: 37
- Missing group: 26

## Navigation Groups

### Administration

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#holiday-admin` | Missing | Holiday Management | Active | VIEW_HOLIDAYS, MANAGE_HOLIDAYS, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1209 |

### Approval / Export / Audit

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#workflow` | Missing | Workflow Operational Readiness | Missing | VIEW_WORKFLOW_OPERATIONAL_READINESS, VIEW_APPROVAL_WORKFLOW, PROJECT_TIME_APPROVAL, VIEW_ACCOUNT_RECONCILIATION, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1286 |
| `#workflow` | Missing | Export Packages | Missing | DOWNLOAD_TIME_EXPORT_PACKAGE, EXPORT_TIME_EXCEL, EXPORT_TIME_PDF, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1293 |
| `#workflow` | Missing | Workflow Audit Evidence | Missing | VIEW_WORKFLOW_AUDIT_EVIDENCE, VIEW_AUDIT_TRAIL, VIEW_ACCOUNT_RECONCILIATION, PROJECT_TIME_APPROVAL, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1300 |
| `#workflow` | Missing | Audit History Events | Missing | VIEW_AUDIT_HISTORY_EVENTS, VIEW_WORKFLOW_AUDIT_EVIDENCE, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1307 |
| `#workflow` | Missing | Workflow Preflight Validation | Missing | VIEW_WORKFLOW_ACTION_CAPABILITIES, PROJECT_TIME_APPROVAL, VIEW_ACCOUNT_RECONCILIATION, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1314 |
| `#workflow` | Production Readiness | Export Package Readiness Summary | Operational | VIEW_EXPORT_PACKAGE_READINESS_SUMMARY, DOWNLOAD_TIME_EXPORT_PACKAGE, EXPORT_TIME_EXCEL, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1328 |
| `#workflow` | Production Readiness | Export Package Evidence Detail | Operational | VIEW_EXPORT_PACKAGE_EVIDENCE_DETAIL, VIEW_WORKFLOW_AUDIT_EVIDENCE, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1335 |
| `#workflow` | Production Readiness | Accounting Reconciliation Workbench | Operational | VIEW_ACCOUNTING_RECONCILIATION_WORKBENCH, MANAGE_ACCOUNT_RECONCILIATION, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1342 |
| `#workflow` | Production Readiness | Locked Period Audit Evidence | Operational | VIEW_LOCKED_PERIOD_AUDIT_EVIDENCE, VIEW_WORKFLOW_AUDIT_EVIDENCE, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1349 |
| `#workflow` | Missing | Workflow Validation Rules | Missing | VIEW_WORKFLOW_VALIDATION_RULES, VIEW_APPROVAL_WORKFLOW, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1373 |
| `#workflow` | Missing | Workflow Operations Center | Missing | VIEW_WORKFLOW_OPERATIONS_CENTER, VIEW_APPROVAL_WORKFLOW, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1380 |
| `#workflow` | Missing | Production Export Evidence | Missing | VIEW_PRODUCTION_EXPORT_EVIDENCE, DOWNLOAD_TIME_EXPORT_PACKAGE, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1394 |

### Approvals

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#manager-approval` | Missing | Approval Inbox | Missing | APPROVE_TIME, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1146 |
| `#workflow` | Missing | Approval / Export / Audit Workflow | Missing | VIEW_APPROVAL_WORKFLOW, PROJECT_TIME_APPROVAL, VIEW_ACCOUNT_RECONCILIATION, MANAGE_ACCOUNT_RECONCILIATION, EXPORT_TIME_EXCEL, EXPORT_TIME_PDF, VIEW_AUDIT_TRAIL, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1153 |

### Audit

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#audit-history` | Missing | Audit History | Active | VIEW_AUDIT_TRAIL, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1216 |

### Compliance

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#time-compliance` | Missing | Time Compliance | Active | VIEW_TIME_COMPLIANCE, MANAGE_TIME_COMPLIANCE, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1202 |

### Cost Control

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#cost-alerts` | Missing | Cost Alert Overrun | Active | VIEW_COST_ALERTS, MANAGE_COST_ALERTS, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1195 |

### Customers

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#customer-directory` | Missing | Customer Directory | Missing | VIEW_CUSTOMERS, MANAGE_CUSTOMERS, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1188 |

### Operations

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#backup-dr` | Missing | Backup / DR Center | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1258 |
| `#backup-retention` | Missing | Backup Retention Center | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1272 |
| `#replication-sync` | Missing | Replication / Sync Status | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1279 |
| `#restore-validation` | Missing | Restore Validation Center | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1265 |
| `#service-control` | Missing | Service Control Center | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1251 |

### Project Delivery

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#project-workspace` | Missing | Project Workspace | Missing | VIEW_PROJECT_WORKSPACE, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1174 |

### Project Intake

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#project-intake` | Missing | Project Intake | Missing | VIEW_PROJECT_INTAKE, MANAGE_PROJECT_INTAKE, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1181 |

### Project Management

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#project-workload` | Missing | Project Workload | Missing | VIEW_PROJECT_WORKLOAD, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1167 |

### Resource Management

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#utilization` | Missing | Utilization | Missing | VIEW_UTILIZATION, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1160 |

### Security

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#azure-admin` | Missing | Azure / Entra Administration | Missing | MANAGE_AZURE_AD, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1244 |
| `#dashboard` | Missing | Engineer Negative Access Smoke | Missing | VIEW_ENGINEER_NEGATIVE_ACCESS_SMOKE, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1415 |
| `#role-admin` | Missing | Role Administration | Missing | VIEW_ROLE_ADMIN_DIRECTORY, MANAGE_ROLES, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1237 |
| `#role-admin` | Production Readiness | Role Access Matrix | Operational | VIEW_ROLE_ACCESS_MATRIX, VIEW_ROLE_ADMIN_DIRECTORY, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1356 |
| `#role-admin` | Missing | Route Permission Contracts | Missing | VIEW_ROUTE_PERMISSION_CONTRACTS, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1401 |
| `#user-admin` | Missing | User Administration | Active | MANAGE_USERS, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1223 |
| `#work-task-builder` | Missing | Work Task Builder | Active | VIEW_WORK_TASK_BUILDER, MANAGE_WORK_TASK_BUILDER, ASSIGN_WORK_TASKS, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1230 |

### System

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#dashboard` | Missing | Dashboard Module Visibility Smoke | Missing | VIEW_MODULE_VISIBILITY_SMOKE, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1321 |
| `#dashboard` | Missing | Production Validation Automation | Missing | VIEW_MODULE_VISIBILITY_SMOKE, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1387 |
| `#dashboard` | Missing | Navigation Registry Integrity Guard | Missing | VIEW_NAVIGATION_REGISTRY_INTEGRITY, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1408 |

### System Operations

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#backup-retention` | Backup Retention | Backup Retention | Operational | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1509 |
| `#production-readiness` | Production Readiness | Production Readiness Command Center | Operational | VIEW_PRODUCTION_READINESS_COMMAND_CENTER, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1363 |
| `#restore-validation` | Restore Validation | Restore Validation | Operational | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1497 |

### Time Management

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#timesheet` | Missing | Timesheet | Missing | None listed | App.jsx:1139 |

### Ungrouped

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#audit-history` | Audit | Audit / Security History | Missing | VIEW_AUDIT_TRAIL, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:774 |
| `#azure-admin` | Azure Admin | Azure / Entra Admin | Missing | VIEW_AZURE_ADMIN, MANAGE_AZURE_SYNC, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:790 |
| `#backup-dr` | Backup / DR | Backup / DR Center | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:822 |
| `#backup-retention` | Backup Retention | Backup Retention | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:838 |
| `#cost-alerts` | Cost Alerts | Cost Overrun Alerts | Missing | VIEW_COST_ALERTS, MANAGE_COST_ALERTS, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:702 |
| `#customer-directory` | Customers | Customer Directory | Missing | VIEW_CUSTOMERS, MANAGE_CUSTOMERS, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:694 |
| `#dashboard` | Missing | Missing | Missing | None listed | App.jsx:902 |
| `#dashboard` | Missing | Missing | Missing | None listed | App.jsx:1028 |
| `#dashboard` | Missing | Missing | Missing | None listed | App.jsx:2794 |
| `#holiday-admin` | Holidays | Holiday Calendar | Missing | VIEW_HOLIDAYS, MANAGE_HOLIDAYS | App.jsx:742 |
| `#manager-approval` | Approvals | Approval Inbox | Missing | VIEW_APPROVAL_INBOX, APPROVE_TIME | App.jsx:726 |
| `#project-allocation-info` | Project Info | Project Allocation and Info | Missing | VIEW_PROJECT_ALLOCATION_INFO, MANAGE_PROJECT_ALLOCATION_INFO, MANAGE_ALL | App.jsx:750 |
| `#project-intake` | Project Intake | Project Intake & Engineering Resource Requests | Missing | VIEW_PROJECT_INTAKE, MANAGE_PROJECT_INTAKE, VIEW_RESOURCE_SCHEDULING, MANAGE_RESOURCE_SCHEDULING, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:686 |
| `#project-workload` | Project Workload | Project Workload | Missing | VIEW_PROJECT_WORKLOAD, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:670 |
| `#project-workspace` | Project Workspace | Project Workspace & Engineering Documents | Missing | VIEW_PROJECT_WORKSPACE, VIEW_ENGINEERING_PROJECT_DOCUMENTS, VIEW_PROJECT_INTAKE, VIEW_RESOURCE_SCHEDULING, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:678 |
| `#psa-modules` | Modules | PSA Modules | Missing | VIEW_PROJECT_INTAKE, VIEW_RESOURCE_SCHEDULING, VIEW_EXPENSES, VIEW_EXECUTIVE_REPORTING | App.jsx:758 |
| `#replication-sync` | Replication / Sync | Replication & Sync Status | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:846 |
| `#restore-validation` | Restore Validation | Restore Validation | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:830 |
| `#role-admin` | Role Admin | Role Administration | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:806 |
| `#service-control` | Services | Service Control Center | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:814 |
| `#time-compliance` | Time Compliance | Time Compliance & Notification Center | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL, VIEW_TIME_COMPLIANCE, VIEW_AUDIT_HISTORY | App.jsx:710 |
| `#timesheet` | Timesheet | Time Entry | Missing | VIEW_TIME_ENTRY | App.jsx:718 |
| `#user-admin` | User Admin | User Administration | Missing | VIEW_USER_ADMIN, MANAGE_USER_ADMIN, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:782 |
| `#utilization` | Utilization | Utilization | Missing | VIEW_OWN_UTILIZATION, VIEW_TEAM_UTILIZATION, VIEW_INDIVIDUAL_UTILIZATION | App.jsx:734 |
| `#work-task-builder` | Work Tasks | Work Task Builder | Missing | VIEW_WORK_TASK_BUILDER, MANAGE_WORK_TASK_BUILDER, ASSIGN_WORK_TASKS, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:798 |
| `#workflow` | Workflow | Workflow | Missing | PROJECT_TIME_APPROVAL, VIEW_APPROVAL_WORKFLOW, VIEW_ACCOUNT_RECONCILIATION, VIEW_WORKFLOW_OPERATIONAL_READINESS, VIEW_WORKFLOW_AUDIT_EVIDENCE, EXPORT_TIME_EXCEL, EXPORT_TIME_PDF, DOWNLOAD_TIME_EXPORT_PACKAGE, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:766 |

## Href Mismatches

| Route | Href | Expected | Source |
|---|---|---|---|
| `workflow` | `#production-readiness` | `#workflow` | App.jsx:1328 |
| `workflow` | `#production-readiness` | `#workflow` | App.jsx:1335 |
| `workflow` | `#production-readiness` | `#workflow` | App.jsx:1342 |
| `workflow` | `#production-readiness` | `#workflow` | App.jsx:1349 |
| `role-admin` | `#production-readiness` | `#role-admin` | App.jsx:1356 |

## Missing Metadata

### Missing href

- `timesheet` at App.jsx:1139
- `manager-approval` at App.jsx:1146
- `workflow` at App.jsx:1153
- `utilization` at App.jsx:1160
- `project-workload` at App.jsx:1167
- `project-workspace` at App.jsx:1174
- `project-intake` at App.jsx:1181
- `customer-directory` at App.jsx:1188
- `cost-alerts` at App.jsx:1195
- `time-compliance` at App.jsx:1202
- `holiday-admin` at App.jsx:1209
- `audit-history` at App.jsx:1216
- `user-admin` at App.jsx:1223
- `work-task-builder` at App.jsx:1230
- `role-admin` at App.jsx:1237
- `azure-admin` at App.jsx:1244
- `service-control` at App.jsx:1251
- `backup-dr` at App.jsx:1258
- `restore-validation` at App.jsx:1265
- `backup-retention` at App.jsx:1272
- `replication-sync` at App.jsx:1279
- `workflow` at App.jsx:1286
- `workflow` at App.jsx:1293
- `workflow` at App.jsx:1300
- `workflow` at App.jsx:1307
- `workflow` at App.jsx:1314
- `dashboard` at App.jsx:1321
- `workflow` at App.jsx:1373
- `workflow` at App.jsx:1380
- `dashboard` at App.jsx:1387
- `workflow` at App.jsx:1394
- `role-admin` at App.jsx:1401
- `dashboard` at App.jsx:1408
- `dashboard` at App.jsx:1415

### Missing title

- `dashboard` at App.jsx:902
- `dashboard` at App.jsx:1028
- `dashboard` at App.jsx:2794

### Missing navLabel

- `dashboard` at App.jsx:902
- `dashboard` at App.jsx:1028
- `timesheet` at App.jsx:1139
- `manager-approval` at App.jsx:1146
- `workflow` at App.jsx:1153
- `utilization` at App.jsx:1160
- `project-workload` at App.jsx:1167
- `project-workspace` at App.jsx:1174
- `project-intake` at App.jsx:1181
- `customer-directory` at App.jsx:1188
- `cost-alerts` at App.jsx:1195
- `time-compliance` at App.jsx:1202
- `holiday-admin` at App.jsx:1209
- `audit-history` at App.jsx:1216
- `user-admin` at App.jsx:1223
- `work-task-builder` at App.jsx:1230
- `role-admin` at App.jsx:1237
- `azure-admin` at App.jsx:1244
- `service-control` at App.jsx:1251
- `backup-dr` at App.jsx:1258
- `restore-validation` at App.jsx:1265
- `backup-retention` at App.jsx:1272
- `replication-sync` at App.jsx:1279
- `workflow` at App.jsx:1286
- `workflow` at App.jsx:1293
- `workflow` at App.jsx:1300
- `workflow` at App.jsx:1307
- `workflow` at App.jsx:1314
- `dashboard` at App.jsx:1321
- `workflow` at App.jsx:1373
- `workflow` at App.jsx:1380
- `dashboard` at App.jsx:1387
- `workflow` at App.jsx:1394
- `role-admin` at App.jsx:1401
- `dashboard` at App.jsx:1408
- `dashboard` at App.jsx:1415
- `dashboard` at App.jsx:2794

### Missing group

- `project-workload` at App.jsx:670
- `project-workspace` at App.jsx:678
- `project-intake` at App.jsx:686
- `customer-directory` at App.jsx:694
- `cost-alerts` at App.jsx:702
- `time-compliance` at App.jsx:710
- `timesheet` at App.jsx:718
- `manager-approval` at App.jsx:726
- `utilization` at App.jsx:734
- `holiday-admin` at App.jsx:742
- `project-allocation-info` at App.jsx:750
- `psa-modules` at App.jsx:758
- `workflow` at App.jsx:766
- `audit-history` at App.jsx:774
- `user-admin` at App.jsx:782
- `azure-admin` at App.jsx:790
- `work-task-builder` at App.jsx:798
- `role-admin` at App.jsx:806
- `service-control` at App.jsx:814
- `backup-dr` at App.jsx:822
- `restore-validation` at App.jsx:830
- `backup-retention` at App.jsx:838
- `replication-sync` at App.jsx:846
- `dashboard` at App.jsx:902
- `dashboard` at App.jsx:1028
- `dashboard` at App.jsx:2794
