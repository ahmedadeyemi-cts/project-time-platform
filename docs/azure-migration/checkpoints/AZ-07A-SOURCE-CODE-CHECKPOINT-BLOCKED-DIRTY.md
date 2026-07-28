# AZ-07A — Source Code Checkpoint Blocked by Uncommitted Changes

Date: 2026-07-12

## Result

The read-only checkpoint completed on the Oracle Linux 9.7 source host.

- Repository root: `/opt/project-time-platform/app/project-time-platform-022`
- Branch: `main`
- HEAD: `5a221da29cdfc1134e5d603175b311ff97658b67`
- Upstream: `origin/main`
- Ahead: `0`
- Behind: `0`
- Tracked modified files: `4`
- Staged files: `0`
- Untracked files: `2`
- Total working-tree entries: `6`
- Dockerfiles: `0`
- Image build allowed: `false`

## Changed paths

1. `deployment/rocky-linux/__pycache__/serve-frontend-local.cpython-39.pyc`
2. `src/backend/ProjectTime.Api/Program.cs`
3. `src/frontend/project-time-web/src/WorkRegisterCenter.jsx`
4. `src/frontend/project-time-web/src/work-register-center.css`
5. `deployment/rocky-linux/projectpulse-055d5a-billing-identifiers-create-edit-ui.sql`
6. `deployment/rocky-linux/projectpulse-055d6b5b-project-lifecycle-sidecar.sql`

## Decision

`BLOCKED_DIRTY`

The application image must not be built from this uncommitted working tree. The generated Python bytecode file should be excluded from the source commit. The five text source and SQL files require a safe secret-pattern scan, whitespace validation, review, and a dedicated application source commit before containerization.

## Safety

The checkpoint did not print patch contents, modify source files, stage changes, commit changes, fetch, reset, stash, clean, build the application, or start an Azure image build.

## Next action

Run `deployment/azure/scripts/az07b-review-dirty-source-safely.sh` or the equivalent inline read-only review on the source host. Do not stage or commit until that review is complete.
