# Module 070 Final Commit Overlap Gate

Module 070 may remain source-only in its isolated worktree. Before any final
commit, its branch must be compared to the then-current integration heads for
Module 002, Module 064, and Module 068. A stale shared file must never win wholesale.

## Required semantic checks

| Shared surface | Mandatory preservation check |
|---|---|
| `docs/MODULE-CATALOG.md` | Retain every current module record and exact Module 064/068 source status |
| `docs/MODULE-WORK-REGISTER.md` | Retain external Module 002 ownership plus 064/068/067/069/070 records |
| `docs/production-readiness/AUGUST_PRODUCTION_READINESS_TRACKER.md` | Consolidate rather than overwrite status supplements |
| `src/backend/ProjectTime.Api/Program.cs` | Preserve all Module 002 endpoints and 064/068 registrations; map 070 once |
| `src/frontend/project-time-web/src/App.jsx` | Preserve Approval Center behavior, every installed route, and 064/068 routes; import/mount/register 070 once |
| `src/frontend/project-time-web/package.json` | Preserve the complete validator chain and append Module 070 exactly once |
| `deployment/containers/web/Dockerfile` | Copy every input required by the combined validator chain |

## Gate outcome

The final commit gate is `BLOCKED` unless evidence names the exact comparison
commits for Modules 002, 064, and 068 and all seven shared surfaces are reviewed.
Conflicts in `Program.cs` or `App.jsx` require a targeted semantic merge. No
rebase, merge, staging, commit, push, deployment, Azure, database, or Entra action
is performed by this source package.
