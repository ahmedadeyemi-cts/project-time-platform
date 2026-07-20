# Module 074 — OEM & Vendor Directory

Status: current-main release-train candidate with source registration; not committed, merged, or deployed.

Module 074 implements tracker requirement **SAL-003** as a US Signal-branded, governed OEM and vendor directory workspace. The source package covers canonical fields, role-aware draft editing, validation, search, and CSV/JSON export without inventing vendor records or changing production data.

## Current boundary

- Every authenticated ProjectPulse user may view the directory surface.
- `SUPER_ADMINISTRATOR`, `ADMINISTRATOR`, `SOLUTION_ARCHITECT`, and `PROJECT_TEAM_COORDINATOR` may create, edit, remove, validate, and export draft records.
- Authority is evaluated from the actual ProjectPulse session so View-As cannot elevate edit access.
- The draft is intentionally unsaved. Database persistence, import, audit history, external synchronization, and public APIs require separate governance decisions.
- `Program.cs`, `App.jsx`, `package.json`, and deployment files are integrated once in the governed release-train workspace.

## Canonical scope

Each vendor record includes vendor name, OEM category, contacts, HTTPS support links, certifications, products, controlled status, optional HTTPS website, and delivery notes. Vendor names must be unique within the draft.

## Validation state

The Module 074 contract validator and isolated frontend component bundle must pass before this source package is considered ready for integration. A .NET 10 build remains a separate required gate in an environment with the .NET 10 SDK.

## External-state record

- Azure changed: no
- Database changed: no
- Entra changed: no
- Deployment performed: no
- Commit created: no
- Push performed: no
