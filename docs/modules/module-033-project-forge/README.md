# Module 033 — Project Forge

Project Forge converts the complete `EXCEL-UltimateProjectManagerDark.xlsb` experience into a governed ProjectPulse module. The workbook is a formula-driven empty template: its project, task, budget, recurrence, and expense input regions contain no business records. Migration 070 therefore creates only Module 033 planning and review structures; it does not seed or copy a project, customer, user, task, assignment, expense, or budget row.

## Workbook coverage

All 15 visible workbook sheets have a dedicated application tab. The application does not reproduce protected spreadsheet helper ranges, circular display/filter dependencies, cached formula errors, or third-party tutorial links.

| Workbook sheet | Project Forge application contract | Authoritative source |
|---|---|---|
| INSTRUCTIONS | Instructions, role scope, review workflow, AI and notification controls | Module metadata and server access response |
| SETUP | Currency, working days, statuses, priorities, phases, project team, and holidays | Governed configuration, `app_users`, `project_assignments`, `company_holidays` |
| OVERALL DASHBOARD | Portfolio, due work, progress, status, effort, and upcoming tasks | `projects`, Forge plans, canonical tasks, assignments, and time |
| MONTHLY CALENDAR | Month task/project calendar, holidays, filters, and completion | Plan tasks, canonical tasks, recurrence projections, company holidays |
| WEEKLY CALENDAR | Seven-day project/task schedule and daily workload | Same normalized schedule projection |
| PROJECT OVERVIEW | Project facts, team, dates, progress, task health, and financial summary | Canonical project and related live data |
| PROJECT MANAGER | Role-scoped project register and PM status view | `projects`, `clients`, authoritative PM relationship |
| PROJECT BUDGET | Labor/material/fixed/travel/equipment/miscellaneous estimates, actual time, expenses, and variance | Forge estimates, Timesheet actuals, Module 005 expenses, project cost authority |
| VARIABLE TASKS | Non-recurring project tasks, priority, assignee, dates, progress, and decisions | Forge plan tasks linked to canonical project tasks |
| RECURRING TASKS | Recurrence rules and reviewable future occurrences | Forge recurrence JSON and normalized occurrence projection |
| TASKS SCHEDULE | Unified date-ordered schedule and dependency projection | Forge plan tasks/dependencies and deterministic FlowHive scheduling |
| TASKS FILTER | Searchable, filterable unified task list | Query-only view of the same task records |
| DECISION MATRIX | Do, Decide, Delegate, and Delete quadrants | Important/urgent flags and governed decision action |
| KANBAN BOARD | Task cards grouped by authoritative task state | Same task rows; a material update is audited and notified |
| GANTT CHART | Task/phase timeline, duration, progress, delays, and holidays | FlowHive schedule engine plus live company holidays |

The workbook formula graph is replaced by one directional model:

1. Canonical projects, identities, assignments, documents, actual time, expenses, cost authority, and holidays are read from their existing owners.
2. Project Forge stores reviewable plans, plan tasks, dependencies, reviewer assignments, and estimates linked to those records.
3. Dashboards, calendars, filters, matrices, Kanban, and Gantt are projections of the same normalized rows.
4. Only an explicit, human-reviewed adoption creates or links canonical `project_tasks` and `project_assignments`.

## Authorization

Authorization is evaluated on every API request. A Project Manager selector in the browser never grants scope.

| Effective role | Project visibility | PM selector | Writes |
|---|---|---|---|
| Super Administrator / Administrator | All canonical projects | All active authoritative PMs | Full Module 033 management and AI |
| Project Management Lead aliases | Projects managed by PMs in governed reporting/team scope | Only those PMs | Manage scoped plans, AI drafts, reviewers, and adoption |
| Project Manager / Project Management | Only `projects.project_manager_user_id = effective user` | None | Manage own project plans, AI drafts, reviewers, and adoption |
| Engineer / Engineering | Only projects/tasks with an active assignment to the effective user | None | Modify only an estimate review assigned to that engineer |
| Administrator View-As | Effective user's read scope | According to effective role | Read-only |

The managed-team scope is derived from live reporting relationships, team/department identity fields, and governed team-scope assignments. No PM or Engineer list is maintained in Project Forge.

## AI through Module 064

Module 064 exposes the `project_forge_plan_estimate` capability for consumer Module 033. Project Forge calls the existing private-first Celar AI composition service in `project_plan` mode and reuses the deterministic Project FlowHive validation and schedule engine.

Authorized project documents—including SOW, GSD, architecture, design, order, quote/proposal, and supporting documents—ground the draft. Project Forge verifies that the selected project has citation-ready private evidence before calling an AI target and refuses to save an uncited or generic plan. Raw private document text is not returned to the browser or sent to an external provider. The selected `project_forge_plan_estimate` route is propagated through Module 064's execution path, so provider changes made for this capability affect Project Forge without changing other AI consumers. The draft retains citations, warnings, conflicts, missing evidence, confidence, and a correlation identifier.

AI converts supported SOW scope lines into cited phases and tasks with descriptions, dependencies, working-day durations, engineering hours, deterministic start and finish dates, and required roles. Those dates are embedded in the isolated review plan before it is saved. AI cannot silently assign an Engineer, publish a canonical task, establish a baseline, change a budget, reserve capacity, or create a customer commitment. Every AI-generated task must be assigned to an eligible Engineer already associated with the project, and every assigned review must be completed before the plan reaches `reviewed`. A Project Manager explicitly adopts that reviewed result.

## Notifications through Module 065

Project Forge does not contain SMTP, Microsoft 365, Brevo, addresses, or a module-specific mail queue. Migration 070 registers these Module 065 policy codes:

- `PROJECT_FORGE_REVIEW_ASSIGNED`
- `PROJECT_FORGE_TASK_ASSIGNED`
- `PROJECT_FORGE_TASK_UPDATED`
- `PROJECT_FORGE_PLAN_UPDATED`

Material plan/task changes and reviewer/assignee changes create idempotent source evidence with project and user identifiers. The existing enterprise notification ledger resolves current recipients from the project team and identity records. Module 065 owns governed Microsoft 365 delivery; Module 032 retains dispatch, retry, failure, and delivery evidence.

## Persistence and audit

Migration `070_module_033_project_forge` owns:

- `project_forge_plans`
- `project_forge_plan_tasks`
- `project_forge_plan_assignments`
- `project_forge_task_dependencies`
- `project_forge_task_details`
- `project_forge_audit_events`

Draft and adoption writes use optimistic revision checks. Audit evidence is append-only. Project/task foreign keys preserve canonical identity, and adoption is explicit so the workbook replacement cannot become a competing project system.

## Release boundary

This PR contains source, migration, rollback, documentation, focused validation, and CI only. It does not apply migration 070, deploy the application, change Azure/Entra configuration, change Module 065 credentials, transmit an email, or merge itself.
