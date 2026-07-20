# Module 066B — Inert Persistence Design

## Status: design only, not applied

No SQL migration, database object, repository adapter, seed, or connection write
exists in this package. `LockedProjectFlowHivePlanRepository` is the only source
implementation and reports `WritesEnabled = false`.

## Proposed entities for a separately authorized phase

| Entity | Purpose | Key controls |
|---|---|---|
| `project_flowhive_plans` | Project-owned plan identity | project FK, lifecycle, current version, row version |
| `project_flowhive_plan_versions` | Immutable draft/baseline/revision snapshots | version/checksum, created actor/time, supersession |
| `project_flowhive_tasks` | Versioned WBS rows | unique plan-version/WBS, canonical-task bridge |
| `project_flowhive_dependencies` | FS/SS/FF/SF edges | unique edge, cycle validation, lead/lag bounds |
| `project_flowhive_assignments` | Versioned identity effort | active user FK, allocation/hour bounds |
| `project_flowhive_baseline_approvals` | Module 002 decision bridge | approval request/decision IDs, actor, immutable checksum |
| `project_flowhive_updates` | Scoped execution updates | actor, assigned scope, old/new values |
| `project_flowhive_collaboration` | Comments/mentions/update requests | visibility scope, revision history, retention |
| `project_flowhive_artifacts` | Generated artifact evidence | version checksum, format, logo checksum, redaction profile |
| `project_flowhive_audit` | Append-only security history | actor/subject/action/correlation/old/new checksums |

## Lifecycle

`draft → in_review → approved_baseline → revision_draft → superseded → closed → archived`

No transition may delete a prior baseline. A rejected review returns to draft
without changing the last approved baseline. Closing and archiving preserve all
versions and artifact checksums.

## Database authorization gate

The schema must be reviewed against current `main`, named migration tooling,
backup/recovery requirements, row-level access, retention, and rollback. Only
after explicit database authorization may a migration be created or applied.
