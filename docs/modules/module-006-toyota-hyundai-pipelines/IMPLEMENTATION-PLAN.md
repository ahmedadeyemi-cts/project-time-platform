# Module 006 — Toyota & Hyundai Pipelines UAT Repair

## Status

- Canonical route: `#toyota-hyundai-pipelines`
- Compatibility routes: `#psa-modules`, `#project-register`
- Source package: Toyota/Hyundai-only reviewed workbook snapshot
- Database migration: none in this repair
- Test deployment: separate guarded action after merge
- Production: unchanged

## Problems corrected

The previously deployed Module 006 read the general `/api/work-register/overview` portfolio. That caused ordinary ProjectPulse projects to appear in the Toyota & Hyundai Pipelines workspace. The table also rendered every matching project at once, creating an unnecessarily long page.

The Modules directory had a separate hard-coded route-to-number map and preferred stale sidebar labels before authoritative registry metadata. That produced **Module number unavailable** on valid modules and could display the wrong number, such as `055B` for Project FlowHive instead of `066`.

## Reviewed workbook snapshot

This repair uses the two Beck workbooks supplied for Module 006 as the immediate Test data source:

- `beck_active_export_2026-07-29.xlsx`
- `beck_archive_export_2026-07-29.xlsx`

The source-controlled snapshot contains:

- **26 active** Toyota or Hyundai records;
- **12 archived / closed** Toyota or Hyundai records;
- **387** historical update events associated with those 38 current records;
- a deterministic immutable UUID for each pipeline record;
- the recognizable workbook Project ID such as `P.0008`;
- customer, business unit, USS owner, project name, quote text, parsed quote numbers, estimated value, update dates, review dates, latest note, first-seen date, and last-import date; and
- historical owner, project-name, quote, review-date, and note context from `Logs` and `Logs History`.

The following source rows are deliberately excluded from active presentation and retained in export evidence for administrator review:

- `P.0051` — Turion, outside the Toyota/Hyundai scope;
- `P.0045` — archived row containing only the placeholder `No Updates`;
- `P.0049` — archived row containing only the placeholder `No Updates`.

## User experience

The workspace now provides:

- Active, Archived / Historical, and All Toyota & Hyundai views;
- customer, status, USS owner, and free-text filters;
- explicit 10, 15, or 25 row pagination;
- a bounded vertically scrollable table rather than an endless page;
- a bounded historical-update timeline per project;
- a multi-sheet Excel-compatible export containing Summary, Active Projects, Archived and Closed, Logs and Audit, Quotes and SELL, and Export Evidence;
- a US Signal print presentation for browser **Save as PDF**; and
- a link to Module 055C for authoritative project/task management.

## Authority boundaries

| Concern | Authority |
|---|---|
| Project creation | Module 055D |
| Existing-project and task mutation | Module 055C |
| Customer records | Module 021 |
| SELL synchronization and credentials | Module 026 |
| Audit and lifecycle evidence | Module 008 and owning project systems |
| Module 006 | Toyota/Hyundai workbook pipeline presentation, reviewed snapshot, filtering, history, and scoped export |

This repair does not silently create ProjectPulse projects or tasks. Assigned Project Managers use Module 055C after an administrator maps a workbook record to its authoritative ProjectPulse project.

## Modules directory and Super Administrator authority

The production build now reconciles module cards against `PROJECTPULSE_MODULES` after all source injectors have registered their modules. The authoritative registry determines:

- module number;
- canonical route;
- display name;
- category; and
- description.

An effective `SUPER_ADMINISTRATOR` receives the complete active registry, including modules not present in a stale or collapsed sidebar. Every card displays the authoritative number and **Full Control · Organization-wide**. Disabled modules remain visible to the Super Administrator.

Administrator View-As remains safe:

- the selected user’s effective roles determine the visible module catalog;
- the underlying administrator’s Super Administrator catalog is not retained;
- availability changes remain disabled during View-As; and
- the backend continues to require the actual user to be Super Administrator for availability mutations.

## Exact-head validation

The temporary source finalizer is removed before the branch is considered ready. The resulting exact PR head must complete the permanent ProjectPulse CI workflow with only the reviewed Module 006 and Modules-directory source files in its comparison to `main`.

## Explicit exclusions

This repair does not:

- add a database-backed workbook import;
- persist reviewer decisions or row fingerprints;
- create a new task repository;
- perform a SELL write or add another SELL credential;
- create immutable database export events;
- apply a migration;
- change Azure or Container Apps;
- deploy to Production; or
- modify the separately active Time Approval PR.

The later database-backed phase will add reviewed import batches, row-level decisions, authoritative project linkage, append-only persistence, immutable export evidence, and assigned-Project-Manager task actions through the existing task authority.
