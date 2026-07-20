# Module 067 — Global Mail Configuration Center

## Purpose

Module 067 is the governed administrative boundary for ProjectPulse outbound
mail. It corrects the status-tracker numbering conflict that assigned this work
to Module 063, which is already installed as Opportunities & Action Tracker.

The module provides a complete read-only source package for:

- non-secret Microsoft 365 mail configuration visibility;
- write-only secret presence and short SHA-256 fingerprint metadata;
- Microsoft Graph and Exchange Online SMTP/OAuth readiness;
- legacy Brevo detection and migration gating;
- shared outbound-mail consumer ownership;
- recipient-environment, retry, idempotency, domain, and connectivity gates;
- actual-session administrator authorization; and
- explicit locks around provider calls, test delivery, activation, and rotation.

## Source status

| Field | Value |
|---|---|
| Module | 067 |
| Route | `global-mail-configuration` |
| API | `/api/global-mail/*` |
| Baseline | `main@2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4` |
| Workspace | `/workspace/scratch/467636bfa6c3/project-time-platform-modules-064-074-release-train` |
| Branch | `feature/modules-064-074-release-train-on-main-20260719` |
| Runtime state | Uncommitted and not deployed |
| Database/Azure/Entra changes | None |

## Authorization boundary

Only the actual ProjectPulse session may establish administrator authority.
View-As never transfers access. Backend authorization requires Administrator or
Super Administrator role, or `SYSTEM_ADMINISTRATION` / `MANAGE_ALL` permission.

## Deliberately locked operations

The following operations are designed and represented in the interface, but are
not executable in this source package:

- Microsoft 365 connectivity calls;
- test email delivery;
- secret creation or rotation;
- provider activation or rollback;
- Send As / Send on Behalf permission changes;
- Brevo disablement;
- recipient or domain changes; and
- Azure, Entra, database, or deployment mutation.

Those actions require separate authorization and evidence. The locked boundary
prevents a source-only module build from becoming an accidental mail cutover.

## Shared-file hold

The full package contains guarded registration changes to `Program.cs`,
`App.jsx`, `package.json`, and the frontend container build context. Those files
overlap Modules 002, 064, 066, and 068. This release-train workspace performs
the semantic integration once from exact current `main`, preserves the protected
validators and routes, and keeps existing mail consumers unchanged.

## Validation evidence

- Module 067 source contract: 57/57 passed.
- .NET 10 Release build: passed with no Module 067 warning or error.
- Module 059 global route guard: passed.
- Module 062 identity guard: passed.
- Module 056E/global-card preservation: passed.
- Production frontend build: passed; the existing chunk-size advisory remains.
- Source status: `RELEASE_TRAIN_CANDIDATE_UNCOMMITTED`; the read-only center is
  registered in source, while provider calls and configuration mutation remain locked.
