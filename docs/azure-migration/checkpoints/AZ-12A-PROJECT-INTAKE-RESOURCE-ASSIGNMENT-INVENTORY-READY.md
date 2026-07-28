# AZ-12A — Project Intake and Resource Assignment Inventory Ready

## Decision

The AZ-11A inventory confirmed that Role Enforcement and User Switcher capabilities already exist extensively in the deployed source revision. The migration will not duplicate that implementation.

The next roadmap phase is Project Intake and Resource Assignment. AZ-12A performs a read-only inventory of the existing backend routes, persistence, frontend components, Work Register behavior, SQL files, and permission-aware workflow references before any implementation branch is created.

## Source control

- Application PR: #11
- Expected application source commit: `abf45bf824747767282f68fa5bd50909f9751eb0`
- Migration branch: `azure-migration/project-health-dashboard-foundation`

## Safety posture

AZ-12A:

- does not change Azure resources;
- does not change PostgreSQL;
- does not rebuild or deploy application images;
- does not modify PR #11;
- does not require the Oracle VM;
- does not expose session tokens, credentials, or connection strings.

## Expected result

```text
PROJECT_INTAKE_RESOURCE_ASSIGNMENT_INVENTORY_RESULT=READY
NEXT_ACTION=RECONCILE_EXISTING_WORKFLOWS_AND_IMPLEMENT_ONLY_CONFIRMED_GAPS
```

## Following phase

After inventory review, create a stacked feature branch only for confirmed gaps in:

- intake capture and status transitions;
- PM ownership and engineering assignment;
- allocation and capacity validation;
- resource scheduling and workload visibility;
- assignment-scoped access enforcement;
- intake, assignment, reassignment, and removal audit events.
