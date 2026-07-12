# AZ-07E — Reviewed Source Branch Ready

Date: 2026-07-12

## Review result

AZ-07D completed with no functional blocking reasons:

- source HEAD matches `5a221da29cdfc1134e5d603175b311ff97658b67`
- six changed paths exactly match the reviewed inventory
- no unexpected paths
- no missing expected text paths
- no duplicate added backend routes
- diff-aware secret review found zero findings in added or untracked text
- worktree and staged whitespace checks passed

## Guarded write action

`deployment/azure/scripts/az07e-create-reviewed-source-branch.sh` is ready for execution on the Oracle Linux source host.

The script requires both guards:

- `PHD_CREATE_REVIEWED_SOURCE_COMMIT=YES`
- `PHD_PUSH_REVIEWED_SOURCE_BRANCH=YES`

## Planned source branch

`source/work-register-billing-lifecycle-20260712`

The script does not commit directly to `main`.

## Exact source scope

Reviewed application and migration files:

- `src/backend/ProjectTime.Api/Program.cs`
- `src/frontend/project-time-web/src/WorkRegisterCenter.jsx`
- `src/frontend/project-time-web/src/work-register-center.css`
- `deployment/rocky-linux/projectpulse-055d5a-billing-identifiers-create-edit-ui.sql`
- `deployment/rocky-linux/projectpulse-055d6b5b-project-lifecycle-sidecar.sql`

Repository hygiene changes:

- add Python bytecode exclusions to `.gitignore`
- restore the generated `.pyc` to HEAD before removing it from version control
- preserve the ignored local `.pyc` file while committing its repository deletion

## Validation

Before committing, the script:

1. revalidates the exact six-path inventory and reviewed SHA-256 hashes
2. verifies `main` is exactly aligned with `origin/main`
3. verifies no staged changes exist
4. creates the dedicated source branch
5. stages only the approved source scope and generated-artifact cleanup
6. runs `git diff --cached --check`
7. copies the worktree to an isolated temporary directory
8. runs a .NET Release restore/build for the `net10.0` API
9. runs `npm ci` and the Vite production build
10. commits and pushes only after both builds pass

No Azure resources, container images, database rows, source services, or production DNS records are modified by AZ-07E.
