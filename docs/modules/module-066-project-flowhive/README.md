# Module 066 — Project FlowHive

## Governed status

- **Source package:** 066A.1 through 066E module-owned source
- **Runtime status:** source-integrated; not merged, deployed, or runtime-verified
- **Persistence status:** locked; no schema or database change was applied
- **AI status:** Module 064 request contract ready; no provider execution
- **Customer-sharing status:** locked; no external link or delivery
- **Baseline:** `main@2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4`
- **Module 002 source:** `f5ede8f6717b01c8f4bf7905b433fead38210007`
- **Branch:** `feature/module-066-complete-integrated-on-main-20260719`

Project FlowHive is ProjectPulse's governed, multi-customer project-planning
workspace. This package combines the validated 066A.1 shared registration with
the fullest safe 066B–066E source. Database persistence, AI-provider execution,
and external customer delivery remain behind explicit authorization gates.

## Delivered source by phase

| Phase | Source delivered | Still gated |
|---|---|---|
| 066A.1 | Endpoint mapping, role-aware route/navigation, installed-module registry, validator/build/container wiring | Commit, review, merge, deployment, and portal verification |
| 066B | WBS, hierarchy, assignments, FS/SS/FF/SF dependencies, lead/lag, validation, locked repository contract, baseline API contract | Database schema approval, repository adapter, authorization and audit persistence |
| 066C | Deterministic weekday schedule, cycle detection, earliest/latest dates, critical path, total/free float, timeline and risk UI | Module 057 holiday/resource calendars and persisted execution updates |
| 066D | Sanitized GSD/SOW request preview and governed local fallback contract | Merged and registered Module 064 `ProjectPulseAiRouter` |
| 066E | Internal draft PDF/XLSX source with the exact repository US Signal logo | Approved baseline authority, customer isolation, expiring links, delivery and access audit |

## Module-owned files

- `src/backend/ProjectTime.Api/Modules/ProjectFlowHiveModule.cs`
- `src/backend/ProjectTime.Api/Modules/ProjectFlowHivePlanningContracts.cs`
- `src/backend/ProjectTime.Api/Modules/ProjectFlowHiveScheduleEngine.cs`
- `src/backend/ProjectTime.Api/Modules/ProjectFlowHiveAiRequestFactory.cs`
- `src/backend/ProjectTime.Api/Modules/ProjectFlowHiveBrandAssets.cs`
- `src/backend/ProjectTime.Api/Modules/ProjectFlowHiveArtifactRenderer.cs`
- `src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx`
- `src/frontend/project-time-web/src/project-flowhive-center.css`
- `src/frontend/project-time-web/scripts/validate-module-066-project-flowhive.mjs`
- `scripts/module-066-validation/*`
- `docs/modules/module-066-project-flowhive/*`

## Safety boundary

The current package intentionally modifies the reviewed shared integration
surfaces required by 066A.1: `Program.cs`, `App.jsx`, the frontend package build
chain, web-container validator context, the central catalog/work register, and
the production-readiness tracker. It does **not** create a database migration,
deployment definition, Azure or Entra change, provider secret, external-sharing
token, notification, or customer delivery.

The planner can edit a draft in browser memory, validate it on the server,
calculate a deterministic schedule, prepare a Module 064 request, and generate
an internally marked artifact. None of those actions stores a plan, establishes
a baseline, calls an AI provider, creates a customer link, sends a message, or
changes external state.

## Reused ProjectPulse authority

- Module 002 Approval Center remains the future plan-baseline approval authority.
- Module 057 remains the future holiday and resource-calendar authority.
- Module 062 provides the effective identity and assignment ID contract.
- Module 064 is the only permitted AI provider router and owns
  Claude → OpenAI → governed local fallback selection.
- Canonical project, task, assignment, and actual-hour data remains read-only.

## US Signal artifact branding

The backend embeds the exact bytes from
`src/frontend/project-time-web/brand/ussignal.jpg` and validates checksum
`c4fc4b33f744d065deeec531f393aa39996273e51eb946a452b1319e6e529183`
before PDF or Excel generation. The frontend uses the existing
`brand/ussignal.png` asset. Generated source artifacts are explicitly marked
`INTERNAL DRAFT — NOT A CUSTOMER BASELINE`.

## Release gates

1. Recheck the source package against then-current `main` before publication.
2. Compare shared surfaces with Modules 002, 062, 064, and all active modules.
3. Route any future FlowHive AI execution only through the registered Module 064
   service and validate the reviewed adapter before enabling provider calls.
4. Obtain database authorization before creating or applying a persistence schema.
5. Add server-side plan/project mutation authorization and immutable audit tests.
6. Complete .NET 10, schedule, artifact, frontend, and warning-delta validation.
7. Complete PDF/XLSX visual QA using US Signal branding.
8. Obtain explicit external-sharing authorization before customer links or delivery.
9. Commit, push, PR, merge, and deploy only under separate explicit authorization.

See the API contract, capability matrix, and security/design documents in this
directory for the complete source boundary.
