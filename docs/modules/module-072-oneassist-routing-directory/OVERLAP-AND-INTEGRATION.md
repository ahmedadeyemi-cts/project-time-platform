# Module 072 Overlap and Integration Gate

## Shared surfaces intentionally untouched

- `src/backend/ProjectTime.Api/Program.cs`
- `src/frontend/project-time-web/src/App.jsx`
- `src/frontend/project-time-web/package.json`
- `deployment/containers/web/Dockerfile`
- the frontend validator chain
- database and deployment directories

## Commit gate

The final commit gate is `BLOCKED` until exact comparisons are refreshed against Module 002, Module 064, Module 067, Module 068, Module 071, Module 059, and Module 062. This is required because registration, public-route allowlisting, the validator chain, and central governance are shared integration surfaces.

Central governance files are changed only in the dedicated governance worktree. Module 072 does not overwrite another module's documentation copy.

## Runtime gate

Runtime activation requires separately authorized retired external compatibility service Access credentials, public-route middleware changes, upstream connectivity validation, and a cutover decision. No database schema is needed for the initial compatibility mode.

## PROJECTPULSE_NATIVE_POSTGRESQL_MIGRATION_031

- Source parent: `603538ad408b70b3e6a26ff2f4f162599fa1cabf`
- Migration source: `database/migrations/031_modules_071_072_native_persistence.sql`
- Rollback source: `database/rollback/031_modules_071_072_native_persistence_rollback.sql`
- Module 071 persistence: ProjectPulse PostgreSQL schedule, roster, acknowledgement, and history tables
- Module 072 persistence: ProjectPulse PostgreSQL routing directory and immutable revision tables
- Platform Administrator authority: explicit
- View-As write authority: blocked
- External compatibility runtime dependency: removed
- Migration applied: no
- Database changed: no
- Deployment performed: no
