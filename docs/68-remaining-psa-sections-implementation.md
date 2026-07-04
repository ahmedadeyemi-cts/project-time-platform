# Remaining PSA Sections Implementation Package

## Purpose

This package prepares the next foundation layer of Project Pulse beyond the validated time-entry and manager-approval workflow.

The sections covered are:

1. Project Intake
2. Project Templates
3. Project Management
4. Project Milestones
5. Project Risks
6. Resource Scheduling
7. Expense Management
8. Client Invoicing
9. Executive Reporting

## Files Added

```text
database/migrations/011_remaining_psa_module_foundation.sql
database/rollback/011_remaining_psa_module_foundation_rollback.sql
deployment/rocky-linux/apply-migration-011.sh
deployment/rocky-linux/apply-remaining-psa-module-api-patch.sh
deployment/rocky-linux/apply-remaining-psa-module-ui-patch.sh
deployment/rocky-linux/project-pulse-remaining-sections-one-time.sh
```

## One-Time Command

```bash
cd /opt/project-time-platform/app/project-time-platform

GIT_SSH_COMMAND='ssh -i ~/.ssh/github_project_time_platform -o IdentitiesOnly=yes' \
git pull

chmod +x deployment/rocky-linux/project-pulse-remaining-sections-one-time.sh
./deployment/rocky-linux/project-pulse-remaining-sections-one-time.sh
```

The script writes its output to:

```text
/tmp/project-pulse-remaining-sections.log
```

## API Endpoints Added

```text
GET /api/project-intake/summary
GET /api/project-management/summary
GET /api/resource-scheduling/capacity?weekStart=YYYY-MM-DD
GET /api/expenses/summary
GET /api/invoicing/summary
GET /api/reporting/executive-dashboard
```

## Database Tables Added

```text
project_intake_requests
project_templates
project_milestones
project_risks
resource_capacity_plans
expense_reports
expense_items
client_invoices
invoice_line_items
reporting_snapshots
```

## UI Added

A new Project Pulse module foundation panel is inserted before the Utilization section. It shows summary cards for:

- Project Intake
- Project Management
- Resource Scheduling
- Expense Management
- Invoicing
- Executive Reporting

It also shows initial milestone and resource capacity detail rows.

## Important Behavior

The package does not remove or replace the validated timesheet workflow. It builds on top of the existing stabilized time-entry, open-task, manager-approval, and row-state behavior.

## Validation

After running the one-time script, validate:

```bash
curl -s http://127.0.0.1:5080/api/version | jq
curl -s http://127.0.0.1:5080/api/project-intake/summary | jq
curl -s http://127.0.0.1:5080/api/project-management/summary | jq
curl -s "http://127.0.0.1:5080/api/resource-scheduling/capacity?weekStart=2026-06-21" | jq
curl -s http://127.0.0.1:5080/api/expenses/summary | jq
curl -s http://127.0.0.1:5080/api/invoicing/summary | jq
curl -s http://127.0.0.1:5080/api/reporting/executive-dashboard | jq
```

Expected API version after this package:

```text
0.5.1
```

## Next Steps After Validation

After the module foundation is visible, the next development sequence should focus on turning each module from read-only summary into controlled workflows:

1. Project Intake create/update/approve request.
2. Project Management milestone and risk CRUD.
3. Resource Scheduling assignment calendar and capacity conflict warnings.
4. Expense Management report creation, receipt metadata, and approval flow.
5. Invoicing labor/expense aggregation from approved time and expenses.
6. Executive dashboards with live metrics from operational tables.
7. Role-based access controls and SSO readiness.

## Status

Ready for execution.
