# Module 069 — Qualifications & Certification Matrix

Module 069 completes the safe read-only workforce capability package available
on the existing ProjectPulse schema. It implements tracker requirements
RES-007 through RES-012 without reusing or removing installed Module 011.

## Package scope

- Role-scoped people, skills, and certification matrix.
- Self, team, and organization visibility derived server-side.
- Identity-backed name, email, team, department, and primary function.
- Qualification category, name, competency, experience, and effective dates.
- Current, expiring-within-90-days, expired, and unrecorded states.
- Search, category, and lifecycle filters.
- Coverage view for identities without qualification records.
- No database migration and no mutation endpoint.

## Governance

| Field | Value |
|---|---|
| Baseline | `main@2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4` |
| Workspace | `/workspace/scratch/467636bfa6c3/project-time-platform-modules-064-074-release-train` |
| Branch | `feature/modules-064-074-release-train-on-main-20260719` |
| Route | `qualifications-certifications` |
| APIs | `/api/qualifications/capabilities`, `/api/qualifications/matrix` |
| Git/runtime | Uncommitted, unpushed, undeployed |
| Azure/database/Entra | No change |

## Identity rule

Module 069 does not create another employee directory. Stable `app_users.user_id`
is the key. Display names and organizational labels remain owned by ProjectPulse
identity/User Administration and follow the Module 062 shared identity approach.

## Deferred mutation scope

Self-service editing, issuer/evidence fields, renewal acknowledgement, renewal
target dates, history, and expiration notifications require separately approved
persistence and audit controls. Notifications also depend on an activated Module
067 shared mail boundary. The current package never mislabels those capabilities
as complete.

## Shared integration hold

Registration/build/governance files overlap Modules 002, 064, 066, and 068.
They are semantically integrated once in the current-main release train and
remain uncommitted pending final validation and publication authority.

## Validation evidence

- Module 069 source contract: 54/54 passed.
- .NET 10 Release build: passed with no Module 069 warning or error.
- Module 059 global route guard: passed.
- Module 062 identity guard: passed.
- Module 056E/global-card preservation: passed.
- Production frontend build: passed; the existing chunk-size advisory remains.
- Source status: `RELEASE_TRAIN_CANDIDATE_UNCOMMITTED`; the read-only route is
  registered in source and remains undeployed.
