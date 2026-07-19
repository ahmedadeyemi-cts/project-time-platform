# Module 074 overlap and integration gate

Module 074 was replayed into the governed release train from baseline `2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4`.

## Module-owned files

- `src/backend/ProjectTime.Api/Modules/OemVendorDirectoryModule.cs`
- `src/frontend/project-time-web/src/OemVendorDirectoryCenter.jsx`
- `src/frontend/project-time-web/src/oem-vendor-directory-center.css`
- `src/frontend/project-time-web/scripts/validate-module-074-oem-vendor-directory.mjs`
- `docs/modules/module-074-oem-vendor-directory/*`

## Deferred shared files

- `src/backend/ProjectTime.Api/Program.cs`
- `src/frontend/project-time-web/src/App.jsx`
- `src/frontend/project-time-web/package.json`
- `deployment/containers/web/Dockerfile`
- central Module Catalog, Module Work Register, and production-readiness tracker

Final commit/integration is **BLOCKED** until exact path and semantic comparisons are run against Module 002, Module 064, Module 068, and then-current `origin/main`. Module 074 must be replayed or integrated from that current baseline; this source branch must not overwrite protected shared-file work.

Database, Azure, Entra, deployment, and external vendor-system changes are outside this package.
