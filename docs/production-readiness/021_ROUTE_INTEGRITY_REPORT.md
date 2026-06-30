# 021 Route Integrity Report

Generated UTC: `2026-06-30T16:18:38.622724+00:00`

## Summary

- Route definitions found: **68**
- Navigation groups found: **17**
- Duplicate routes: **22**
- Duplicate hrefs: **3**
- Missing href: **40**
- Missing title: **3**
- Missing navLabel: **43**
- Missing group: **26**
- Href mismatches: **0**

## Status Counts

- Active: **6**
- Missing: **60**
- Operational: **2**

## Production Operations Route Configs

- `dashboard`
- `workflow`

## Integrity Findings

- Duplicate route keys: audit-history, azure-admin, backup-dr, backup-retention, cost-alerts, customer-directory, dashboard, holiday-admin, manager-approval, project-intake, project-workload, project-workspace, replication-sync, restore-validation, role-admin, service-control, time-compliance, timesheet, user-admin, utilization, work-task-builder, workflow
- Duplicate hrefs: #backup-retention, #dashboard, #restore-validation
- Missing href: 40
- Missing title: 3
- Missing navLabel: 43
- Missing group: 26

## Navigation Groups

### Administration

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#holiday-admin` | Missing | Holiday Management | Active | VIEW_HOLIDAYS, MANAGE_HOLIDAYS, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1207 |

### Approval / Export / Audit

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#workflow` | Missing | Workflow Operational Readiness | Missing | VIEW_WORKFLOW_OPERATIONAL_READINESS, VIEW_APPROVAL_WORKFLOW, PROJECT_TIME_APPROVAL, VIEW_ACCOUNT_RECONCILIATION, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1284 |
| `#workflow` | Missing | Export Packages | Missing | DOWNLOAD_TIME_EXPORT_PACKAGE, EXPORT_TIME_EXCEL, EXPORT_TIME_PDF, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1291 |
| `#workflow` | Missing | Workflow Audit Evidence | Missing | VIEW_WORKFLOW_AUDIT_EVIDENCE, VIEW_AUDIT_TRAIL, VIEW_ACCOUNT_RECONCILIATION, PROJECT_TIME_APPROVAL, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1298 |
| `#workflow` | Missing | Audit History Events | Missing | VIEW_AUDIT_HISTORY_EVENTS, VIEW_WORKFLOW_AUDIT_EVIDENCE, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1305 |
| `#workflow` | Missing | Workflow Preflight Validation | Missing | VIEW_WORKFLOW_ACTION_CAPABILITIES, PROJECT_TIME_APPROVAL, VIEW_ACCOUNT_RECONCILIATION, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1312 |
| `#workflow` | Missing | Export Package Readiness Summary | Missing | VIEW_EXPORT_PACKAGE_READINESS_SUMMARY, DOWNLOAD_TIME_EXPORT_PACKAGE, EXPORT_TIME_EXCEL, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1326 |
| `#workflow` | Missing | Export Package Evidence Detail | Missing | VIEW_EXPORT_PACKAGE_EVIDENCE_DETAIL, VIEW_WORKFLOW_AUDIT_EVIDENCE, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1333 |
| `#workflow` | Missing | Accounting Reconciliation Workbench | Missing | VIEW_ACCOUNTING_RECONCILIATION_WORKBENCH, MANAGE_ACCOUNT_RECONCILIATION, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1340 |
| `#workflow` | Missing | Locked Period Audit Evidence | Missing | VIEW_LOCKED_PERIOD_AUDIT_EVIDENCE, VIEW_WORKFLOW_AUDIT_EVIDENCE, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1347 |
| `#workflow` | Missing | Workflow Validation Rules | Missing | VIEW_WORKFLOW_VALIDATION_RULES, VIEW_APPROVAL_WORKFLOW, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1368 |
| `#workflow` | Missing | Workflow Operations Center | Missing | VIEW_WORKFLOW_OPERATIONS_CENTER, VIEW_APPROVAL_WORKFLOW, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1375 |
| `#workflow` | Missing | Production Export Evidence | Missing | VIEW_PRODUCTION_EXPORT_EVIDENCE, DOWNLOAD_TIME_EXPORT_PACKAGE, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1389 |

### Approvals

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#manager-approval` | Missing | Approval Inbox | Missing | APPROVE_TIME, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1144 |
| `#workflow` | Missing | Approval / Export / Audit Workflow | Missing | VIEW_APPROVAL_WORKFLOW, PROJECT_TIME_APPROVAL, VIEW_ACCOUNT_RECONCILIATION, MANAGE_ACCOUNT_RECONCILIATION, EXPORT_TIME_EXCEL, EXPORT_TIME_PDF, VIEW_AUDIT_TRAIL, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1151 |

### Audit

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#audit-history` | Missing | Audit History | Active | VIEW_AUDIT_TRAIL, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1214 |

### Compliance

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#time-compliance` | Missing | Time Compliance | Active | VIEW_TIME_COMPLIANCE, MANAGE_TIME_COMPLIANCE, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1200 |

### Cost Control

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#cost-alerts` | Missing | Cost Alert Overrun | Active | VIEW_COST_ALERTS, MANAGE_COST_ALERTS, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1193 |

### Customers

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#customer-directory` | Missing | Customer Directory | Missing | VIEW_CUSTOMERS, MANAGE_CUSTOMERS, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1186 |

### Operations

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#backup-dr` | Missing | Backup / DR Center | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1256 |
| `#backup-retention` | Missing | Backup Retention Center | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1270 |
| `#replication-sync` | Missing | Replication / Sync Status | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1277 |
| `#restore-validation` | Missing | Restore Validation Center | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1263 |
| `#service-control` | Missing | Service Control Center | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1249 |

### Project Delivery

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#project-workspace` | Missing | Project Workspace | Missing | VIEW_PROJECT_WORKSPACE, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1172 |

### Project Intake

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#project-intake` | Missing | Project Intake | Missing | VIEW_PROJECT_INTAKE, MANAGE_PROJECT_INTAKE, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1179 |

### Project Management

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#project-workload` | Missing | Project Workload | Missing | VIEW_PROJECT_WORKLOAD, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1165 |

### Resource Management

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#utilization` | Missing | Utilization | Missing | VIEW_UTILIZATION, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1158 |

### Security

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#azure-admin` | Missing | Azure / Entra Administration | Missing | MANAGE_AZURE_AD, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1242 |
| `#dashboard` | Missing | Engineer Negative Access Smoke | Missing | VIEW_ENGINEER_NEGATIVE_ACCESS_SMOKE, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1410 |
| `#role-admin` | Missing | Role Administration | Missing | VIEW_ROLE_ADMIN_DIRECTORY, MANAGE_ROLES, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1235 |
| `#role-admin` | Missing | Role Access Matrix | Missing | VIEW_ROLE_ACCESS_MATRIX, VIEW_ROLE_ADMIN_DIRECTORY, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1354 |
| `#role-admin` | Missing | Route Permission Contracts | Missing | VIEW_ROUTE_PERMISSION_CONTRACTS, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1396 |
| `#user-admin` | Missing | User Administration | Active | MANAGE_USERS, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1221 |
| `#work-task-builder` | Missing | Work Task Builder | Active | VIEW_WORK_TASK_BUILDER, MANAGE_WORK_TASK_BUILDER, ASSIGN_WORK_TASKS, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1228 |

### System

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#dashboard` | Missing | Dashboard Module Visibility Smoke | Missing | VIEW_MODULE_VISIBILITY_SMOKE, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1319 |
| `#dashboard` | Missing | Production Readiness Command Center | Missing | VIEW_PRODUCTION_READINESS_COMMAND_CENTER, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1361 |
| `#dashboard` | Missing | Production Validation Automation | Missing | VIEW_MODULE_VISIBILITY_SMOKE, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1382 |
| `#dashboard` | Missing | Navigation Registry Integrity Guard | Missing | VIEW_NAVIGATION_REGISTRY_INTEGRITY, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1403 |

### System Operations

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#backup-retention` | Backup Retention | Backup Retention | Operational | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1503 |
| `#restore-validation` | Restore Validation | Restore Validation | Operational | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:1491 |

### Time Management

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#timesheet` | Missing | Timesheet | Missing | None listed | App.jsx:1137 |

### Ungrouped

| Route | Label | Title | Status | Permissions | Source |
|---|---|---|---|---|---|
| `#audit-history` | Audit | Audit / Security History | Missing | VIEW_AUDIT_TRAIL, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:772 |
| `#azure-admin` | Azure Admin | Azure / Entra Admin | Missing | VIEW_AZURE_ADMIN, MANAGE_AZURE_SYNC, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:788 |
| `#backup-dr` | Backup / DR | Backup / DR Center | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:820 |
| `#backup-retention` | Backup Retention | Backup Retention | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:836 |
| `#cost-alerts` | Cost Alerts | Cost Overrun Alerts | Missing | VIEW_COST_ALERTS, MANAGE_COST_ALERTS, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:700 |
| `#customer-directory` | Customers | Customer Directory | Missing | VIEW_CUSTOMERS, MANAGE_CUSTOMERS, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:692 |
| `#dashboard` | Missing | Missing | Missing | None listed | App.jsx:900 |
| `#dashboard` | Missing | Missing | Missing | None listed | App.jsx:1026 |
| `#dashboard` | Missing | Missing | Missing | None listed | App.jsx:2788 |
| `#holiday-admin` | Holidays | Holiday Calendar | Missing | VIEW_HOLIDAYS, MANAGE_HOLIDAYS | App.jsx:740 |
| `#manager-approval` | Approvals | Approval Inbox | Missing | VIEW_APPROVAL_INBOX, APPROVE_TIME | App.jsx:724 |
| `#project-allocation-info` | Project Info | Project Allocation and Info | Missing | VIEW_PROJECT_ALLOCATION_INFO, MANAGE_PROJECT_ALLOCATION_INFO, MANAGE_ALL | App.jsx:748 |
| `#project-intake` | Project Intake | Project Intake & Engineering Resource Requests | Missing | VIEW_PROJECT_INTAKE, MANAGE_PROJECT_INTAKE, VIEW_RESOURCE_SCHEDULING, MANAGE_RESOURCE_SCHEDULING, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:684 |
| `#project-workload` | Project Workload | Project Workload | Missing | VIEW_PROJECT_WORKLOAD, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:668 |
| `#project-workspace` | Project Workspace | Project Workspace & Engineering Documents | Missing | VIEW_PROJECT_WORKSPACE, VIEW_ENGINEERING_PROJECT_DOCUMENTS, VIEW_PROJECT_INTAKE, VIEW_RESOURCE_SCHEDULING, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:676 |
| `#psa-modules` | Modules | PSA Modules | Missing | VIEW_PROJECT_INTAKE, VIEW_RESOURCE_SCHEDULING, VIEW_EXPENSES, VIEW_EXECUTIVE_REPORTING | App.jsx:756 |
| `#replication-sync` | Replication / Sync | Replication & Sync Status | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:844 |
| `#restore-validation` | Restore Validation | Restore Validation | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:828 |
| `#role-admin` | Role Admin | Role Administration | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:804 |
| `#service-control` | Services | Service Control Center | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:812 |
| `#time-compliance` | Time Compliance | Time Compliance & Notification Center | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL, VIEW_TIME_COMPLIANCE, VIEW_AUDIT_HISTORY | App.jsx:708 |
| `#timesheet` | Timesheet | Time Entry | Missing | VIEW_TIME_ENTRY | App.jsx:716 |
| `#user-admin` | User Admin | User Administration | Missing | VIEW_USER_ADMIN, MANAGE_USER_ADMIN, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:780 |
| `#utilization` | Utilization | Utilization | Missing | VIEW_OWN_UTILIZATION, VIEW_TEAM_UTILIZATION, VIEW_INDIVIDUAL_UTILIZATION | App.jsx:732 |
| `#work-task-builder` | Work Tasks | Work Task Builder | Missing | VIEW_WORK_TASK_BUILDER, MANAGE_WORK_TASK_BUILDER, ASSIGN_WORK_TASKS, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:796 |
| `#workflow` | Workflow | Workflow | Missing | PROJECT_TIME_APPROVAL, VIEW_APPROVAL_WORKFLOW, VIEW_ACCOUNT_RECONCILIATION, VIEW_WORKFLOW_OPERATIONAL_READINESS, VIEW_WORKFLOW_AUDIT_EVIDENCE, EXPORT_TIME_EXCEL, EXPORT_TIME_PDF, DOWNLOAD_TIME_EXPORT_PACKAGE, SYSTEM_ADMINISTRATION, MANAGE_ALL | App.jsx:764 |

## Href Mismatches

- None.

## Missing Metadata

### Missing href

- `timesheet` at App.jsx:1137
- `manager-approval` at App.jsx:1144
- `workflow` at App.jsx:1151
- `utilization` at App.jsx:1158
- `project-workload` at App.jsx:1165
- `project-workspace` at App.jsx:1172
- `project-intake` at App.jsx:1179
- `customer-directory` at App.jsx:1186
- `cost-alerts` at App.jsx:1193
- `time-compliance` at App.jsx:1200
- `holiday-admin` at App.jsx:1207
- `audit-history` at App.jsx:1214
- `user-admin` at App.jsx:1221
- `work-task-builder` at App.jsx:1228
- `role-admin` at App.jsx:1235
- `azure-admin` at App.jsx:1242
- `service-control` at App.jsx:1249
- `backup-dr` at App.jsx:1256
- `restore-validation` at App.jsx:1263
- `backup-retention` at App.jsx:1270
- `replication-sync` at App.jsx:1277
- `workflow` at App.jsx:1284
- `workflow` at App.jsx:1291
- `workflow` at App.jsx:1298
- `workflow` at App.jsx:1305
- `workflow` at App.jsx:1312
- `dashboard` at App.jsx:1319
- `workflow` at App.jsx:1326
- `workflow` at App.jsx:1333
- `workflow` at App.jsx:1340
- `workflow` at App.jsx:1347
- `role-admin` at App.jsx:1354
- `dashboard` at App.jsx:1361
- `workflow` at App.jsx:1368
- `workflow` at App.jsx:1375
- `dashboard` at App.jsx:1382
- `workflow` at App.jsx:1389
- `role-admin` at App.jsx:1396
- `dashboard` at App.jsx:1403
- `dashboard` at App.jsx:1410

### Missing title

- `dashboard` at App.jsx:900
- `dashboard` at App.jsx:1026
- `dashboard` at App.jsx:2788

### Missing navLabel

- `dashboard` at App.jsx:900
- `dashboard` at App.jsx:1026
- `timesheet` at App.jsx:1137
- `manager-approval` at App.jsx:1144
- `workflow` at App.jsx:1151
- `utilization` at App.jsx:1158
- `project-workload` at App.jsx:1165
- `project-workspace` at App.jsx:1172
- `project-intake` at App.jsx:1179
- `customer-directory` at App.jsx:1186
- `cost-alerts` at App.jsx:1193
- `time-compliance` at App.jsx:1200
- `holiday-admin` at App.jsx:1207
- `audit-history` at App.jsx:1214
- `user-admin` at App.jsx:1221
- `work-task-builder` at App.jsx:1228
- `role-admin` at App.jsx:1235
- `azure-admin` at App.jsx:1242
- `service-control` at App.jsx:1249
- `backup-dr` at App.jsx:1256
- `restore-validation` at App.jsx:1263
- `backup-retention` at App.jsx:1270
- `replication-sync` at App.jsx:1277
- `workflow` at App.jsx:1284
- `workflow` at App.jsx:1291
- `workflow` at App.jsx:1298
- `workflow` at App.jsx:1305
- `workflow` at App.jsx:1312
- `dashboard` at App.jsx:1319
- `workflow` at App.jsx:1326
- `workflow` at App.jsx:1333
- `workflow` at App.jsx:1340
- `workflow` at App.jsx:1347
- `role-admin` at App.jsx:1354
- `dashboard` at App.jsx:1361
- `workflow` at App.jsx:1368
- `workflow` at App.jsx:1375
- `dashboard` at App.jsx:1382
- `workflow` at App.jsx:1389
- `role-admin` at App.jsx:1396
- `dashboard` at App.jsx:1403
- `dashboard` at App.jsx:1410
- `dashboard` at App.jsx:2788

### Missing group

- `project-workload` at App.jsx:668
- `project-workspace` at App.jsx:676
- `project-intake` at App.jsx:684
- `customer-directory` at App.jsx:692
- `cost-alerts` at App.jsx:700
- `time-compliance` at App.jsx:708
- `timesheet` at App.jsx:716
- `manager-approval` at App.jsx:724
- `utilization` at App.jsx:732
- `holiday-admin` at App.jsx:740
- `project-allocation-info` at App.jsx:748
- `psa-modules` at App.jsx:756
- `workflow` at App.jsx:764
- `audit-history` at App.jsx:772
- `user-admin` at App.jsx:780
- `azure-admin` at App.jsx:788
- `work-task-builder` at App.jsx:796
- `role-admin` at App.jsx:804
- `service-control` at App.jsx:812
- `backup-dr` at App.jsx:820
- `restore-validation` at App.jsx:828
- `backup-retention` at App.jsx:836
- `replication-sync` at App.jsx:844
- `dashboard` at App.jsx:900
- `dashboard` at App.jsx:1026
- `dashboard` at App.jsx:2788
