# ProjectPulse Module Work Register

## Register authority

This file is the central source work register for concurrent ProjectPulse module
development. Central-register ownership is assigned to the Module 066A workspace
for the current coordination cycle. Individual module workspaces continue to own
their module-specific README and evidence.

## Current forward-moving source baseline

| Field | Value |
|---|---|
| Base branch | `main` |
| Base commit | `04fcafd4f49840428645e537db7de436e34b1c88` |
| Base description | Merges Module 062 unified identity profile and presence while preserving the recovered module baseline and leaving Module 002 unchanged |
| Source status | Approved base for new isolated work as of 2026-07-19 |
| Deployment status | Not asserted by this register update |
| Prior approved baseline | `main@92c0964afdc26dede72e09bf2c8d7c0629126bc0` |
| Governance lineage | `docs/module-development-governance-20260717@66cf0f6457efaa33196f2c91b03bd3a35d13bf19` |

The current `main` commit contains the prior approved baseline. New modules must
start from current `main` or a later verified forward-moving commit.

## Active work ownership

| Module/area | Status | Workspace | Branch | Base | Confirmed scope | Expected files/areas | GitHub | Azure/DB/Entra |
|---|---|---|---|---|---|---|---|---|
| 001 | Active in external chat; details not independently reported | Not reported to central register | Not reported to central register | Must be current `main` | Timesheet preservation and follow-up work | Timesheet plus shared integration files as required | No success asserted | Not authorized here |
| 002 | Active, changed workspace observed | `/home/ahmed/project-time-platform-module-002-integration-20260718T234150Z` | `feature/module-002-on-recovered-main-20260718T234150Z` | `92c0964afdc26dede72e09bf2c8d7c0629126bc0` | Approval Center semantic integration while protecting PM, correction, password-reset, and history workflows | `Program.cs`, `App.jsx`, approval components, `package.json`, styles, documentation | Commit/push not asserted | Not authorized here |
| 062 | Completed and merged through PR 19 | `/home/ahmed/project-time-platform-module-062-20260719T001319Z` | `feature/module-062-unified-identity-profile-20260719T001319Z` | `92c0964afdc26dede72e09bf2c8d7c0629126bc0` | Unified identity profile, photograph, normalized presence, and Profile/057/059 integration | Module 062 files and reviewed shared integration | Head `3852a21e1098de9ad907e3da91e0646d99adcb7c`; merged as `04fcafd4f49840428645e537db7de436e34b1c88` | No Azure, database, or Entra change reported by PR 19 |
| 066A | Active | `/workspace/project-time-platform-module-066-project-flowhive` | `feature/module-066-project-flowhive-foundation-20260719` | `92c0964afdc26dede72e09bf2c8d7c0629126bc0` | Read-only Project FlowHive portfolio, task grid, access scope, capability matrix, and API contract | New Module 066 backend/frontend/validator/docs; central register and catalog | Local only; commit/push not authorized | No Azure, DB application, or Entra change |
| Central governance | Active under 066A ownership | `/workspace/project-time-platform-module-066-project-flowhive` | `feature/module-066-project-flowhive-foundation-20260719` | `92c0964afdc26dede72e09bf2c8d7c0629126bc0` | Maintain current module work register and catalog | `docs/MODULE-WORK-REGISTER.md`, `docs/MODULE-CATALOG.md` | Local only | Not applicable |

`Not reported` is an evidence state, not a placeholder path to use in a command.

## Shared-file integration hold

These files or areas have active or likely overlap and require a single guarded
integration pass after module checkpoints:

| Shared target | Active consumers | Current rule |
|---|---|---|
| `src/backend/ProjectTime.Api/Program.cs` | 001, 002, 066, PR 12; Module 062 is now on `main` | Do not overwrite; integrate registrations semantically from the then-current `main` |
| `src/frontend/project-time-web/src/App.jsx` | 001, 002, 066, route recovery; Module 062 is now on `main` | Preserve every installed route, Module 062 consumers, and Module 059 global placement |
| `src/frontend/project-time-web/package.json` | 002, 066, Module 059/062 guards | Combine validation scripts intentionally |
| `src/frontend/project-time-web/src/styles.css` | Multiple active modules | New modules use module-scoped CSS; avoid bulk global replacement |
| `src/frontend/project-time-web/src/SystemUserGuide.jsx` | Module 999 and future modules | Update only after final route and behavior are stable |
| `docs/production-readiness/AUGUST_PRODUCTION_READINESS_TRACKER.md` | 002 and central readiness reporting | Consolidate once active module checkpoints are known |
| `src/backend/ProjectTime.Api/Assets/Branding/` | 002/062 lineage and future 066 exports | Use only verified approved US Signal logo assets |

## Module 066A conflict review

Initial Module 066A files are new paths and have no direct path overlap with the
observed Module 001, Module 002, merged Module 062, or PR 12 changes. Shared
registration is deferred, so the foundation cannot remove or shadow existing
routes. Module 066B must start from the then-current `main` after Module 002's
semantic merge rather than replaying shared files from the 066A base.

## External integration risks

| Item | State | Risk | Required treatment |
|---|---|---|---|
| PR 12 — Module 042 | Open draft on older base | Touches `Program.cs`, `App.jsx`, `package.json`, and shared styles | Rebuild or semantically forward-integrate on current `main` before merging |
| PR 10 — Azure foundation | Open on substantially older base | Large infrastructure history and deployment scope | Keep outside application module integration unless explicitly authorized |
| Legacy mislabeled Module 062 branch, excluding the PR 19 branch | Diverged historical lineage | Mislabeled Module 059 lineage | Preserve for evidence; do not confuse it with the merged PR 19 implementation |

## Branding control

Every future ProjectPulse PDF or Excel artifact must use the approved US Signal
logo supplied to the project. A text-only mark, improvised logo, or unverified
asset from a stale branch is not acceptable. Logo introduction and shared artifact
rendering require file-level review on the current source baseline.

## Baseline advancement history

| Date | Previous baseline | New source baseline | Reason |
|---|---|---|---|
| 2026-07-17 | `9e23b792c9f2b627d2b8fdca8539bca5505bec2d` | `c651dc71228cda89d42cf0fa4224371082e07a38` | Module 059 restored on current Module 060 source |
| 2026-07-18 | `c651dc71228cda89d42cf0fa4224371082e07a38` | `92c0964afdc26dede72e09bf2c8d7c0629126bc0` | Modules 024–030 and 058 registry restored, Module 059 guard restored, Module 999 and route enumeration restored |
| 2026-07-19 | `92c0964afdc26dede72e09bf2c8d7c0629126bc0` | `04fcafd4f49840428645e537db7de436e34b1c88` | PR 19 merged Module 062 unified identity profile and presence with final head `3852a21e1098de9ad907e3da91e0646d99adcb7c`; Module 002 remained unchanged |

## Current authorization record

For Module 066A:

- implementation authorized: yes;
- central register/catalog ownership authorized: yes;
- commit authorized: no;
- push authorized: no;
- deployment authorized: no;
- Azure changes authorized: no;
- database application or schema changes authorized: no;
- Entra changes authorized: no.
