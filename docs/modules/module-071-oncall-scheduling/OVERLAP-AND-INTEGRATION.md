# Module 071 Overlap and Integration Gate

## Shared surfaces intentionally untouched

- `src/backend/ProjectTime.Api/Program.cs`
- `src/frontend/project-time-web/src/App.jsx`
- `src/frontend/project-time-web/package.json`
- `deployment/containers/web/Dockerfile`
- the frontend validator chain
- all database and deployment directories

## Required comparison owners

The final commit gate is `BLOCKED` until exact overlap checks are refreshed against:

- Module 002 Approval Center integration,
- Module 064 Shared AI Configuration,
- Module 067 Global Mail Configuration and sender contract,
- Module 068 System Architecture,
- Module 059 global shell placement,
- Module 062 identity profile and actual/effective session behavior.

Central governance files are updated only in the dedicated governance worktree. Module 071 does not edit another module's central documentation copy.

## Later integration files

After the gate clears, a dedicated integration branch may change `Program.cs`, `App.jsx`, `package.json`, the container validator context, and central governance documents. Those changes must be semantically merged from the then-current `main`; this source branch must not overwrite them wholesale.
