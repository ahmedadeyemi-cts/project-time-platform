# Module 066 — Project FlowHive

## Current phase

- **Phase:** 066A — Read-only foundation
- **Status:** Active, uncommitted, not pushed, not deployed
- **Base:** `main@92c0964afdc26dede72e09bf2c8d7c0629126bc0`
- **Branch:** `feature/module-066-project-flowhive-foundation-20260719`

## Purpose

Project FlowHive is the governed, multi-customer project-planning workspace for
ProjectPulse. Its approved product direction includes WBS planning, dependencies,
schedule views, controlled baselines, assignments, collaboration, execution
updates, portfolio reporting, customer-safe sharing, and historical plan control.

The product goal is a cohesive Smartsheet-class planning experience integrated
with ProjectPulse. It is not a claim of literal parity with every unrelated
Smartsheet product.

## Phase 066A outcome

Phase 066A creates a safe foundation without changing the database or activating
planning mutations. It provides:

No database migration is introduced or applied by this phase.

- an authenticated capability endpoint;
- an authenticated, server-scoped portfolio endpoint;
- read-only canonical project, task, assignment, and hour summaries;
- PM managed-project scope;
- engineer assigned-project and assigned-task scope;
- Project Team Coordinator and authorized leadership business scope;
- View-As-aware effective-user resolution;
- a read-only React portfolio, task grid, and capability-plan component;
- explicit labels for functionality that is still planned;
- a permanent capability matrix and API contract;
- a validator that protects the 066A read-only boundary.

Task codes are displayed as canonical task references. Phase 066A does not
represent those codes as approved WBS numbers.

## Module-owned implementation

- `src/backend/ProjectTime.Api/Modules/ProjectFlowHiveModule.cs`
- `src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx`
- `src/frontend/project-time-web/src/project-flowhive-center.css`
- `src/frontend/project-time-web/scripts/validate-module-066-project-flowhive.mjs`
- `docs/modules/module-066-project-flowhive/README.md`
- `docs/modules/module-066-project-flowhive/CAPABILITY-MATRIX.md`
- `docs/modules/module-066-project-flowhive/API-CONTRACT.md`

## API foundation

- `GET /api/project-flowhive/capabilities`
- `GET /api/project-flowhive/portfolio`

Both endpoints require an authenticated ProjectPulse session. The portfolio
endpoint filters records on the server; frontend filtering is not an access
control boundary.

## Existing ProjectPulse foundations reused

- `clients`
- `projects`
- `project_tasks`
- `project_assignments`
- `time_entries`
- `app_users`
- `app_user_role_assignments`
- `app_roles`
- `reporting_relationships`
- Project Workspace role-scope patterns
- Work Register intake, documents, assignments, changes, and closure foundations
- Calendar Capacity resource context
- Timesheet project/task eligibility and actual-hour records

## Explicitly unavailable in 066A

- planning record creation or editing;
- controlled WBS hierarchy;
- dependency creation or scheduling calculations;
- Gantt, critical path, float, or schedule constraints;
- baseline approval, revision, supersession, or archive persistence;
- comments, mentions, update requests, or plan attachments;
- GSD/SOW AI plan generation;
- Outlook scheduling;
- customer links or external approval;
- PDF or Excel export;
- new database objects;
- Azure, database, Entra, or deployment operations.

## Deferred shared integration

The module files intentionally remain unregistered while Modules 001, 002, and
062 are active in separate workspaces. A later guarded integration will review:

- `src/backend/ProjectTime.Api/Program.cs`;
- `src/frontend/project-time-web/src/App.jsx`;
- `src/frontend/project-time-web/package.json`;
- `src/frontend/project-time-web/src/SystemUserGuide.jsx`;
- the current production-readiness tracker.

No shared integration file is modified in Phase 066A foundation staging.

## Export branding requirement

Any future Project FlowHive PDF or Excel artifact must use the approved US Signal
logo supplied for ProjectPulse. Text-only approximations or unapproved substitute
marks are not acceptable. Export work remains blocked until the verified logo
asset is available on the current forward-moving source baseline.

## Tracker requirements represented

- GOV-015
- RBAC-019
- WRK-011
- AI-008
- AI-019
- RPT-013

See [CAPABILITY-MATRIX.md](./CAPABILITY-MATRIX.md) for phasing and acceptance
evidence and [API-CONTRACT.md](./API-CONTRACT.md) for the 066A response contract.
