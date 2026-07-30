# Module 006 — Toyota & Hyundai Pipelines Implementation Plan

## Status

- Issue: #274
- Branch: `feature/module-006-toyota-hyundai-pipelines-20260729`
- Current phase: read-only source foundation
- Canonical route: `#toyota-hyundai-pipelines`
- Compatibility route: `#psa-modules`
- Database migration: none in this phase
- Deployment: none

## Problem being corrected

Module 006 was previously exposed as the customer-specific **Toyota & Hyundai Pipeline** and inherited the older `PSA Modules` placeholder. The presentation was generated only into the dashboard and did not mount a dedicated Module 006 route. This did not provide an enterprise Toyota & Hyundai Pipelines.

## Phase 1 — Read-only Toyota & Hyundai Pipelines foundation

This branch establishes the canonical Module 006 identity and a dedicated route. The initial register:

- reads the existing `/api/work-register/overview` contract;
- keeps only authoritative `projects` records;
- provides Active, Archived / Historical, and All views;
- supports project, customer, status, Project Manager, engineer, and SELL-reference search;
- displays project ownership, dates, task/document counts, allocated and used hours, cost, remaining cost, and burn state;
- loads project details and lifecycle history from the existing Work Register and lifecycle APIs;
- preserves archived projects as read-only evidence;
- links authorized management actions to Module 055C; and
- creates no alternate project, task, assignment, financial, customer, SELL, or audit system.

The legacy `#psa-modules` address is normalized to the canonical `#toyota-hyundai-pipelines` route. The retired Toyota & Hyundai identity remains explicit history rather than an active enterprise label.

## Authority boundaries

| Concern | Authority |
|---|---|
| Project creation | Module 055D |
| Existing-project mutation | Module 055C |
| Project/customer records | Existing Work Register and Module 021 |
| SELL/customer synchronization | Module 026 |
| Lifecycle and immutable change evidence | Existing lifecycle and Module 008 audit contracts |
| Enterprise reports | Module 030 |
| Module 006 | Scoped project inventory, navigation, future reviewed import, and scoped export |

Module 006 is read-only in Phase 1. View-As remains read-only because the backend continues to enforce actual/effective-user and project scope.

## Phase 2 — Beck workbook import

This phase cannot start from assumptions about the workbook. The exact workbook must be available for column-level review. The source package will then add:

- an upload and parsing boundary for the approved file type;
- a versioned field-mapping contract;
- preview rows before persistence;
- valid, warning, duplicate, unresolved, and rejected classifications;
- idempotent matching by authoritative project/customer identifiers;
- no silent project creation;
- source file name, checksum, sheet, row, actor, timestamp, decision, and resulting project identity;
- import batches and immutable row-level evidence; and
- a rollback/recovery process for an interrupted import.

No workbook row will persist until a reviewer accepts the mapping and row decisions.

## Phase 3 — US Signal Excel and PDF exports

The export package will be source-controlled and auditable. It will include:

- current user/project scope;
- current register filters;
- active or historical view selection;
- as-of timestamp;
- release and export schema version;
- row count and source identity;
- approved US Signal branding;
- Excel workbook output;
- PDF register output; and
- immutable export audit evidence.

The current interface keeps import and export controls disabled until the database evidence schema and artifact validation are reviewed.

## Phase 4 — Database metadata and permissions

A separate migration will be proposed only after the import and export schemas are reviewed. It is expected to address:

- canonical Module 006 name and route in database-backed role-policy catalogs;
- compatibility metadata for the retired route;
- Toyota & Hyundai Pipelines view/import/export permissions;
- import batches and import-row evidence;
- export events and artifact evidence; and
- immutable audit triggers.

The migration must pass apply, idempotence, rollback, reapply, permission, and immutability tests before it can be authorized for Test.

## Validation gates

Phase 1 requires:

- Module 006 source validator;
- generated App route and mount validation;
- complete frontend production build;
- Module 012/037 name and route regression;
- Modules 055C/055D ownership regression;
- Modules 021/026 customer and SELL regression;
- active/archive behavior;
- project-scope behavior;
- responsive and dark-theme behavior; and
- no migration, deployment, or environment mutation.

Later phases add workbook fixtures, duplicate/idempotence tests, database migration tests, Excel/PDF structure checks, branding checks, audit immutability, guarded Test deployment, and authenticated UAT.

## Explicit exclusions from Phase 1

- no database change;
- no workbook persistence;
- no Excel or PDF generation;
- no project/task mutation;
- no customer or SELL mutation;
- no Module 030 report duplication;
- no Azure or Container Apps operation;
- no Test or Production deployment.
