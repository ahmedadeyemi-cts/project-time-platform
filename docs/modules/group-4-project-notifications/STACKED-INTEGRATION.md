# Group 4 Stacked Integration Note

Group 4 is intentionally based on `feature/group-3-unified-project-financial-workspaces-20260728` at `1b0d539ed0b346244faeb298472ce6f8c52bca17`.

The Group 4 pull request should initially target that Group 3 branch so its review shows only Group 4-owned source. After Group 3 is merged, Group 4 must be updated normally from the resulting `main`, conflicts must be inspected manually, and all migration, API, frontend, Module 065, container-context, and ProjectPulse CI validations must be rerun.

The shared integration files are:

- `src/backend/ProjectTime.Api/ProjectTime.Api.csproj`
- `src/frontend/project-time-web/package.json`

The Group 3 financial endpoint registration, injector, and validator must remain exactly once while Group 4 compatibility middleware, endpoint registration, injector, and validator are added exactly once.

No force-push over newer work, migration execution, merge, deployment preparation, or deployment is authorized by this package.
