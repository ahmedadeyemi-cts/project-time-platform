# Module 006 — Toyota & Hyundai Pipelines Standalone Management

## Authority

- Canonical route: `#toyota-hyundai-pipelines`
- Compatibility routes: `#psa-modules`, `#project-register`
- Persistence foundation: Migration `068_module006_standalone_pipeline_management`
- Additional-customer expansion: Migration `069_module006_customer_pipeline_expansion`
- Owning module: Module 006
- Module 055C dependency: **none**
- Production: unchanged until a separately approved release

Module 006 owns its reviewed Toyota and Hyundai pipeline baseline, additional customer pipeline records, standalone tasks, status updates, review dates, and append-only note history. It does not create, open, or modify Module 055C records.

## Source continuity

The reviewed Beck workbook snapshot remains the initial read model:

- `beck_active_export_2026-07-29.xlsx`
- `beck_archive_export_2026-07-29.xlsx`
- 26 active Toyota or Hyundai rows
- 12 archived / closed Toyota or Hyundai rows
- 387 historical update events

Migration 068 adds database overlays for reviewed snapshot rows and durable records for newly created Module 006 projects. A snapshot row becomes editable when its first change, task, or new note is saved. Its deterministic pipeline UUID remains the Module 006 identity.

Migration 069 preserves that baseline while replacing the Toyota/Hyundai-only database constraint with a governed customer-name contract. Customer names are trimmed, limited to 2–120 characters, and cannot contain control characters. The rollback refuses to proceed once additional-customer data exists, preventing accidental loss or invalidation of saved records.

## User experience

The standalone workspace provides:

- Active, Archived / Historical, and All views
- an All customers filter populated from current records
- Toyota, Hyundai, and additional customer pipeline records in the same governed workspace
- a customer field that supports choosing an existing customer or typing a new customer name
- status, USS owner, and free-text filters
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
- Migration 069 changes only the Module 006 customer-name constraint and adds a customer lookup index.
- API and database validation enforce the same bounded customer-name rules.

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

Every response identifies Module 006 as the authority and reports `linkedToModule055C = false`. The pipeline read contract reports `customerEntryMode = extensible` and the supported maximum customer-name length.

## Finalized UAT follow-up source boundary

The exact source finalizer completed successfully before this documentation checkpoint. It also converged the associated UAT repairs:

- Module 005 lifecycle and PM-acceptance endpoints are registered in the compiled safe endpoint map, and base upload history remains visible when lifecycle enrichment fails independently.
- Module 005 upload-history columns and action controls use bounded responsive sizing, and the PM acceptance button remains visibly identified.
- Module 039 receives the `compact` source-health property through every render layer, preventing the post-refresh blank-page exception.
- Module 055C visibly renders and copies the persisted immutable business identifier in each result row and selected-project header.
- Module 065 uses a React-owned Open module button that navigates directly to `#entra-secret-administration` and visually matches the other module-card actions.
- The More menu uses a responsive enterprise application-launcher layout while preserving fail-closed, server-backed role and View-As permission evidence.
- The top bar uses the approved stacked US Signal logo asset already embedded for branded documents.
- Temporary source-finalizer and trigger workflows were removed from the final application boundary.

## Release validation

Before merge, the exact head must pass:

- Migration 068 foundation and rollback contract validation
- Migration 069 apply, additional-customer insert, guarded rollback, and safe reapply validation
- .NET Release compilation
- frontend syntax and production build
- Module 006 standalone source contract
- Module 006 extensible customer validation at the API, database, and generated UI layers
- no Module 055C links or task handoffs in Module 006
- role and View-As checks
- generated-source convergence
- approved stacked-logo convergence
- web-container build
- repository security posture
