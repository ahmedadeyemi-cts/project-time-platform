# Module 998 Overlap and Integration Record

## Verified base

- Current source: `origin/main@3d9a3dca8af479c854dc4c4a9294bc8aad273074`.
- Required checkpoint: `48421d5ba1584d64fc3bd043304c003eff1dc27b`.
- The required checkpoint is a verified ancestor of the source base.
- The only post-checkpoint main change is the PR 25 web-container validator
  context correction; it is preserved.

## Shared surfaces

| File | Module 998 change | Preservation rule |
|---|---|---|
| `Program.cs` | Add one endpoint-map call | Preserve every existing map and helper |
| `App.jsx` | Add one import, role-aware navigation/registry records, route mount, and shell exclusion | Preserve all routes and Module 059 placement |
| `package.json` | Append Module 998 after the existing 059→062→002→064–074 chain | Do not reorder or remove a validator |
| Web `Dockerfile` | Copy the Module 998 backend and docs into the validator build context | Preserve PR 25 Module 002 correction |
| Module Catalog / Work Register / Status Tracker | Record source ownership and fail-closed state | Do not claim merge, deployment, or execution |

## Protected modules

Module 998 must preserve Modules 002, 056E, 059, 062, and 064–074 by passing
their validators and the Module 056E suppression guard. No Module 002 or 064–074 owned component is
edited. Shared edits are additive and reviewed at the exact path level.

## Module 997 sequencing

Module 997 must use its own isolated worktree and branch from the then-current
`origin/main`. It may reference Module 998 contracts, but it must not depend on
an unmerged Module 998 branch or copy Module 998 shared edits blindly. Any
overlap is replayed semantically after verifying the current base.
