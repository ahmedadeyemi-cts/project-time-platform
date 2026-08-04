# Module 006 — Toyota & Hyundai Pipelines Standalone Management

## Authority

- Canonical route: `#toyota-hyundai-pipelines`
- Compatibility routes: `#psa-modules`, `#project-register`
- Persistence: Migration `068_module006_standalone_pipeline_management`
- Owning module: Module 006
- Module 055C dependency: **none**
- Production: unchanged until a separately approved release

Module 006 owns its Toyota and Hyundai pipeline records, standalone tasks, status updates, review dates, and append-only note history. It does not create, open, or modify Module 055C records.

## Source continuity

The reviewed Beck workbook snapshot remains the initial read model:

- `beck_active_export_2026-07-29.xlsx`
- `beck_archive_export_2026-07-29.xlsx`
- 26 active Toyota or Hyundai rows
- 12 archived / closed Toyota or Hyundai rows
- 387 historical update events

The migration adds database overlays for reviewed snapshot rows and durable records for newly created Module 006 projects. A snapshot row becomes editable when its first change, task, or new note is saved. Its deterministic pipeline UUID remains the Module 006 identity.

## User experience

The standalone workspace provides:

- Active, Archived / Historical, and All views
- Toyota/Hyundai, status, USS owner, and free-text filters
- 10, 15, and 25 row pagination
- bounded tables and history panels
- row-level Open / Edit
- project detail editing for Business Unit, customer, USS owner, project name, quote numbers, estimated value, status, update date, and next-review date
- append-only status updates and notes
- standalone task/action-item creation and editing
- task assignment text, due date, status, archive, and restore controls
- Add New Project
- project archive and restore
- multi-sheet Excel-compatible export for Projects, Updates and Notes, and Tasks
- browser Print / Save PDF

The design follows the supplied legacy workflow examples:

1. A filterable dashboard with project rows and row actions.
2. A project editor containing update date, next-review date, scrollable history, and a new status-note field.
3. A separate Add New Project workflow.
4. A project detail view that preserves prior notes while accepting a new update.

## Security and concurrency

- Writes are permitted only to approved Project Management, PTC, Sales, Administrator, and Super Administrator roles.
- Administrator View-As is read-only.
- Project and task updates use optimistic revision checks.
- Project notes, task events, and lifecycle evidence are append-only.
- Migration 068 does not copy or reference Module 055C project identifiers.

## API contract

```text
GET  /api/module-006/pipeline
POST /api/module-006/pipeline
PUT  /api/module-006/pipeline/{recordId}
POST /api/module-006/pipeline/{recordId}/updates
POST /api/module-006/pipeline/{recordId}/archive

GET  /api/module-006/tasks
POST /api/module-006/pipeline/{recordId}/tasks
PUT  /api/module-006/pipeline/{recordId}/tasks/{taskId}
POST /api/module-006/pipeline/{recordId}/tasks/{taskId}/archive
```

Every response identifies Module 006 as the authority and reports `linkedToModule055C = false`.

## Release validation

Before merge, the exact head must pass:

- Migration 068 and rollback contract validation
- .NET Release compilation
- frontend syntax and production build
- Module 006 standalone source contract
- no Module 055C links or task handoffs in Module 006
- role and View-As checks
- generated-source convergence
- web-container build
- repository security posture
