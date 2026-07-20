# Module 066 — Overlap and Release Gates

## Verified package base

- `main`: `2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4`
- Module 002 source: `f5ede8f6717b01c8f4bf7905b433fead38210007`
- Module 002 merge: `2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4`

## Module-owned paths

This package changes `ProjectFlowHive*` backend/frontend files, the Module 066
validator, and `docs/modules/module-066-project-flowhive/`.

## Reviewed shared surfaces

- `src/backend/ProjectTime.Api/Program.cs`
- `src/frontend/project-time-web/src/App.jsx`
- `src/frontend/project-time-web/package.json`
- `deployment/containers/web/Dockerfile`
- `docs/MODULE-CATALOG.md`
- `docs/MODULE-WORK-REGISTER.md`
- `docs/production-readiness/AUGUST_PRODUCTION_READINESS_TRACKER.md`

These changes are intentionally integrated in this branch from the exact
Module 002-enabled main. The package preserves Module 002's Approval Center,
Module 059's global boundary, Module 062's identity integration, every existing
route, and the protected frontend validator order.

## Current source-worktree overlap evidence

- Modules 064, 067, 068, 069, and 070 overlap on the same seven reviewed shared
  surfaces: `Program.cs`, `App.jsx`, `package.json`, the web Dockerfile, the
  module catalog, the work register, and the production-readiness tracker.
- Modules 065 and 071–080 have no exact changed-path overlap with this package.
- No other worktree changes a Module 066-owned `ProjectFlowHive*` source path.

The shared-surface packages must be serialized or semantically combined from a
new then-current main. They must not be bulk-committed independently and merged
without a fresh overlap comparison.

## Mandatory final comparison

Before a commit-ready integration is staged, compare the then-current main and
active worktrees for Modules 002, 062, 064, 065, 067–080, and any later module.
Preserve every current route, validator, container copy input, Module 059 global
shell boundary, and Approval Center workflow. Stop with conflict evidence if a
semantic merge is not unambiguous.
