# 021 Role-Based Production Readiness Runbooks

Generated UTC: `2026-06-30T16:26:40.409560+00:00`

## Purpose

These runbooks define role-based production readiness validation paths for Project Health Dashboard / ChangePoint. They are designed to confirm that each major persona can access the correct workflows, that restricted actions remain controlled, and that production-critical evidence is visible before release candidate validation.

## Execution Guidance

Use these runbooks during production readiness validation. For each persona, validate route visibility, role enforcement, workflow clarity, evidence availability, and auditability. Record findings as release-hardening issues before final release candidate validation.

## Administrator / System Owner

**Objective:** Validate platform governance, role enforcement, production readiness command center, operational controls, and auditability.

### Recommended Route Review

| Route | Label | Group | Status | Permission Signal |
|---|---|---|---|---|
| `#audit-history` | Audit | Ungrouped | Missing | VIEW_AUDIT_TRAIL, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#azure-admin` | Azure Admin | Ungrouped | Missing | VIEW_AZURE_ADMIN, MANAGE_AZURE_SYNC, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#backup-dr` | Backup / DR | Ungrouped | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#backup-retention` | Backup Retention | Ungrouped | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#cost-alerts` | Cost Alerts | Ungrouped | Missing | VIEW_COST_ALERTS, MANAGE_COST_ALERTS, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#customer-directory` | Customers | Ungrouped | Missing | VIEW_CUSTOMERS, MANAGE_CUSTOMERS, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#dashboard` | dashboard | Ungrouped | Missing | No explicit permission listed |
| `#holiday-admin` | Holidays | Ungrouped | Missing | VIEW_HOLIDAYS, MANAGE_HOLIDAYS |
| `#manager-approval` | Approvals | Ungrouped | Missing | VIEW_APPROVAL_INBOX, APPROVE_TIME |
| `#project-allocation-info` | Project Info | Ungrouped | Missing | VIEW_PROJECT_ALLOCATION_INFO, MANAGE_PROJECT_ALLOCATION_INFO, MANAGE_ALL |

### Validation Steps

1. Confirm the user is authenticated with Administrator-level access.
2. Open the production readiness command center and verify operational status cards are visible.
3. Open role/security administration and confirm the role matrix aligns with expected access boundaries.
4. Review user administration and confirm inactive or restricted users do not appear as active production operators.
5. Use the View-As preview only as a read-only access verification tool.
6. Open audit history and confirm administrative and workflow activities are traceable.
7. Record any route, permission, or visibility gap as a production readiness issue.

### Acceptance Criteria

- Administrator can access governance, audit, production operations, and readiness surfaces.
- View-As behavior remains read-only for write actions.
- Protected routes do not expose data to unauthenticated users.
- Audit evidence is available for sensitive workflow and administrative areas.

## Project Manager

**Objective:** Validate customer-to-project intake, resource assignment readiness, project allocation, task handoff, and approval preparation.

### Recommended Route Review

| Route | Label | Group | Status | Permission Signal |
|---|---|---|---|---|
| `#backup-dr` | Backup / DR | Ungrouped | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#cost-alerts` | Cost Alerts | Ungrouped | Missing | VIEW_COST_ALERTS, MANAGE_COST_ALERTS, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#customer-directory` | Customers | Ungrouped | Missing | VIEW_CUSTOMERS, MANAGE_CUSTOMERS, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#manager-approval` | Approvals | Ungrouped | Missing | VIEW_APPROVAL_INBOX, APPROVE_TIME |
| `#project-allocation-info` | Project Info | Ungrouped | Missing | VIEW_PROJECT_ALLOCATION_INFO, MANAGE_PROJECT_ALLOCATION_INFO, MANAGE_ALL |
| `#project-intake` | Project Intake | Ungrouped | Missing | VIEW_PROJECT_INTAKE, MANAGE_PROJECT_INTAKE, VIEW_RESOURCE_SCHEDULING, MANAGE_RESOURCE_SCHEDULING, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#project-workload` | Project Workload | Ungrouped | Missing | VIEW_PROJECT_WORKLOAD, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#project-workspace` | Project Workspace | Ungrouped | Missing | VIEW_PROJECT_WORKSPACE, VIEW_ENGINEERING_PROJECT_DOCUMENTS, VIEW_PROJECT_INTAKE, VIEW_RESOURCE_SCHEDULING, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#psa-modules` | Modules | Ungrouped | Missing | VIEW_PROJECT_INTAKE, VIEW_RESOURCE_SCHEDULING, VIEW_EXPENSES, VIEW_EXECUTIVE_REPORTING |
| `#time-compliance` | Time Compliance | Ungrouped | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL, VIEW_TIME_COMPLIANCE, VIEW_AUDIT_HISTORY |

### Validation Steps

1. Confirm the user can access customer and project intake context appropriate to the role.
2. Review project intake summary and verify records have clear status and ownership.
3. Open resource assignment or allocation views and validate staffing/capacity information is understandable.
4. Review work-task handoff and confirm planning details can move into execution.
5. Confirm approval readiness indicators are visible before work moves to downstream approval or export handling.
6. Record any missing data, unclear empty state, or route visibility mismatch.

### Acceptance Criteria

- Project intake data is understandable and operationally actionable.
- Resource assignment views support staffing decisions.
- Project handoff information is visible without requiring manual spreadsheet reconciliation.
- Approval readiness is clear before downstream workflow actions.

## Manager / Approver

**Objective:** Validate approval queue visibility, review controls, exception handling, unlock decisions, and audit accountability.

### Recommended Route Review

| Route | Label | Group | Status | Permission Signal |
|---|---|---|---|---|
| `#audit-history` | Audit | Ungrouped | Missing | VIEW_AUDIT_TRAIL, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#azure-admin` | Azure Admin | Ungrouped | Missing | VIEW_AZURE_ADMIN, MANAGE_AZURE_SYNC, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#backup-retention` | Backup Retention | Ungrouped | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#cost-alerts` | Cost Alerts | Ungrouped | Missing | VIEW_COST_ALERTS, MANAGE_COST_ALERTS, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#manager-approval` | Approvals | Ungrouped | Missing | VIEW_APPROVAL_INBOX, APPROVE_TIME |
| `#project-intake` | Project Intake | Ungrouped | Missing | VIEW_PROJECT_INTAKE, MANAGE_PROJECT_INTAKE, VIEW_RESOURCE_SCHEDULING, MANAGE_RESOURCE_SCHEDULING, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#project-workload` | Project Workload | Ungrouped | Missing | VIEW_PROJECT_WORKLOAD, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#project-workspace` | Project Workspace | Ungrouped | Missing | VIEW_PROJECT_WORKSPACE, VIEW_ENGINEERING_PROJECT_DOCUMENTS, VIEW_PROJECT_INTAKE, VIEW_RESOURCE_SCHEDULING, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#psa-modules` | Modules | Ungrouped | Missing | VIEW_PROJECT_INTAKE, VIEW_RESOURCE_SCHEDULING, VIEW_EXPENSES, VIEW_EXECUTIVE_REPORTING |
| `#replication-sync` | Replication / Sync | Ungrouped | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL |

### Validation Steps

1. Confirm the user can access approval queues and pending review counts.
2. Review one approval-ready item and verify the displayed information supports an approval decision.
3. Validate exception or unlock controls are restricted and clearly labeled.
4. Confirm approval actions produce traceable audit evidence.
5. Verify unauthenticated access to approval routes returns the expected protected response.
6. Record any missing approval evidence, confusing label, or role mismatch.

### Acceptance Criteria

- Manager can identify pending approvals quickly.
- Approval and unlock actions are controlled by role.
- Exception handling is visible without bypassing accountability.
- Audit history can support review of approval activity.

## Engineer / Contributor

**Objective:** Validate time entry, assigned work visibility, project/non-project activity handling, and submission readiness.

### Recommended Route Review

| Route | Label | Group | Status | Permission Signal |
|---|---|---|---|---|
| `#backup-dr` | Backup / DR | Ungrouped | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#cost-alerts` | Cost Alerts | Ungrouped | Missing | VIEW_COST_ALERTS, MANAGE_COST_ALERTS, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#customer-directory` | Customers | Ungrouped | Missing | VIEW_CUSTOMERS, MANAGE_CUSTOMERS, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#holiday-admin` | Holidays | Ungrouped | Missing | VIEW_HOLIDAYS, MANAGE_HOLIDAYS |
| `#manager-approval` | Approvals | Ungrouped | Missing | VIEW_APPROVAL_INBOX, APPROVE_TIME |
| `#project-allocation-info` | Project Info | Ungrouped | Missing | VIEW_PROJECT_ALLOCATION_INFO, MANAGE_PROJECT_ALLOCATION_INFO, MANAGE_ALL |
| `#project-intake` | Project Intake | Ungrouped | Missing | VIEW_PROJECT_INTAKE, MANAGE_PROJECT_INTAKE, VIEW_RESOURCE_SCHEDULING, MANAGE_RESOURCE_SCHEDULING, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#project-workload` | Project Workload | Ungrouped | Missing | VIEW_PROJECT_WORKLOAD, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#project-workspace` | Project Workspace | Ungrouped | Missing | VIEW_PROJECT_WORKSPACE, VIEW_ENGINEERING_PROJECT_DOCUMENTS, VIEW_PROJECT_INTAKE, VIEW_RESOURCE_SCHEDULING, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#psa-modules` | Modules | Ungrouped | Missing | VIEW_PROJECT_INTAKE, VIEW_RESOURCE_SCHEDULING, VIEW_EXPENSES, VIEW_EXECUTIVE_REPORTING |

### Validation Steps

1. Confirm the user can access the correct time-entry or assigned-work area.
2. Review assigned project work and validate it is distinguishable from non-project activity.
3. Confirm week/date context is understandable and supports accurate entry.
4. Check whether validation, holidays, preferences, or hidden-row behavior improves entry accuracy.
5. Confirm submission readiness is clear before work enters approval routing.
6. Record any work visibility gap, unclear validation message, or missing route permission.

### Acceptance Criteria

- Engineer can find assigned work and time-entry context.
- Project and non-project work are clearly separated.
- Submission readiness is understandable.
- The contributor experience supports accurate downstream approval and export.

## Accounting / Export Reviewer

**Objective:** Validate export package readiness, reconciliation evidence, protected export actions, and downstream reporting controls.

### Recommended Route Review

| Route | Label | Group | Status | Permission Signal |
|---|---|---|---|---|
| `#audit-history` | Audit | Ungrouped | Missing | VIEW_AUDIT_TRAIL, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#manager-approval` | Approvals | Ungrouped | Missing | VIEW_APPROVAL_INBOX, APPROVE_TIME |
| `#project-workspace` | Project Workspace | Ungrouped | Missing | VIEW_PROJECT_WORKSPACE, VIEW_ENGINEERING_PROJECT_DOCUMENTS, VIEW_PROJECT_INTAKE, VIEW_RESOURCE_SCHEDULING, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#psa-modules` | Modules | Ungrouped | Missing | VIEW_PROJECT_INTAKE, VIEW_RESOURCE_SCHEDULING, VIEW_EXPENSES, VIEW_EXECUTIVE_REPORTING |
| `#time-compliance` | Time Compliance | Ungrouped | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL, VIEW_TIME_COMPLIANCE, VIEW_AUDIT_HISTORY |
| `#timesheet` | Timesheet | Ungrouped | Missing | VIEW_TIME_ENTRY |
| `#work-task-builder` | Work Tasks | Ungrouped | Missing | VIEW_WORK_TASK_BUILDER, MANAGE_WORK_TASK_BUILDER, ASSIGN_WORK_TASKS, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#workflow` | Workflow | Ungrouped | Missing | PROJECT_TIME_APPROVAL, VIEW_APPROVAL_WORKFLOW, VIEW_ACCOUNT_RECONCILIATION, VIEW_WORKFLOW_OPERATIONAL_READINESS, VIEW_WORKFLOW_AUDIT_EVIDENCE, EXPORT_TIME_EXCEL, EXPORT_TIME_PDF, DOWNLOAD_TIME_EXPORT_PACKAGE, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#holiday-admin` | Holiday Management | Administration | Active | VIEW_HOLIDAYS, MANAGE_HOLIDAYS, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#customer-directory` | Customer Directory | Customers | Missing | VIEW_CUSTOMERS, MANAGE_CUSTOMERS, SYSTEM_ADMINISTRATION, MANAGE_ALL |

### Validation Steps

1. Confirm the user can access export readiness and accounting review surfaces.
2. Review export package status and validate readiness indicators are understandable.
3. Confirm reconciliation evidence is visible before export actions.
4. Validate export/download actions are protected by role.
5. Review audit history for export-related traceability.
6. Record any missing export evidence, unclear reconciliation status, or role enforcement issue.

### Acceptance Criteria

- Accounting can identify export-ready work.
- Export actions are controlled and traceable.
- Reconciliation evidence is available before downstream handoff.
- Audit history supports accounting review.

## Read-Only Stakeholder

**Objective:** Validate safe visibility for leadership, auditors, or stakeholders who need status awareness without write access.

### Recommended Route Review

| Route | Label | Group | Status | Permission Signal |
|---|---|---|---|---|
| `#audit-history` | Audit | Ungrouped | Missing | VIEW_AUDIT_TRAIL, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#azure-admin` | Azure Admin | Ungrouped | Missing | VIEW_AZURE_ADMIN, MANAGE_AZURE_SYNC, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#backup-dr` | Backup / DR | Ungrouped | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#backup-retention` | Backup Retention | Ungrouped | Missing | SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#cost-alerts` | Cost Alerts | Ungrouped | Missing | VIEW_COST_ALERTS, MANAGE_COST_ALERTS, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#customer-directory` | Customers | Ungrouped | Missing | VIEW_CUSTOMERS, MANAGE_CUSTOMERS, SYSTEM_ADMINISTRATION, MANAGE_ALL |
| `#dashboard` | dashboard | Ungrouped | Missing | No explicit permission listed |
| `#holiday-admin` | Holidays | Ungrouped | Missing | VIEW_HOLIDAYS, MANAGE_HOLIDAYS |
| `#manager-approval` | Approvals | Ungrouped | Missing | VIEW_APPROVAL_INBOX, APPROVE_TIME |
| `#project-allocation-info` | Project Info | Ungrouped | Missing | VIEW_PROJECT_ALLOCATION_INFO, MANAGE_PROJECT_ALLOCATION_INFO, MANAGE_ALL |

### Validation Steps

1. Confirm the user can access only appropriate read-oriented surfaces.
2. Open the production readiness or reporting dashboard and review high-level status.
3. Open project or workflow summary views without performing write actions.
4. Confirm restricted actions are hidden, disabled, or rejected according to role enforcement.
5. Review audit/reporting visibility for transparency.
6. Record any overexposed action, missing status summary, or route visibility issue.

### Acceptance Criteria

- Stakeholder can view appropriate status information.
- Write actions are unavailable or denied.
- Production readiness status is understandable without operational permissions.
- Role-based visibility supports transparency without weakening control.

## Cross-Role Production Readiness Sequence

1. Start with Administrator / System Owner to validate governance, access, and readiness command-center controls.
2. Validate Project Manager workflow from customer/project intake through resource assignment and handoff.
3. Validate Engineer / Contributor workflow for assigned work and time-entry readiness.
4. Validate Manager / Approver workflow for approvals, exceptions, and auditability.
5. Validate Accounting / Export Reviewer workflow for export readiness and reconciliation evidence.
6. Validate Read-Only Stakeholder access to confirm transparency without write capability.

## 021D Validation Notes

- This is a production readiness documentation and static-route mapping pass.
- Full browser validation remains deferred until the final 021 release-candidate validation.
- Recommended routes are generated from the 021B route integrity inventory.
- If a recommended route appears under the wrong role, update route metadata or permission mapping in a later 021 hardening pass.
