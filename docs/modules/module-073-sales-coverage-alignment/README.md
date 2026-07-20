# Module 073 — Sales Coverage Alignment

Module 073 implements the source-only SAL-002 workspace for Account Executive-to-Resale Operations primary/backup and Solution Architect alignment. It preserves current ProjectPulse AE/SA relationships as read-only source signals and provides an identity-backed, effective-dated draft editor.

## Confirmed authorization

- Every authenticated ProjectPulse user can view current source signals.
- Canonical `SUPER_ADMINISTRATOR`, `ADMINISTRATOR`, `SOLUTION_ARCHITECT`, and `PROJECT_TEAM_COORDINATOR` roles can create, edit, remove, validate, and export draft relationships.
- All mutation authority uses the actual session; View-As does not transfer editing authority.

## Source package

- Stable ProjectPulse user IDs and role-aware dropdowns for Account Executive, primary/backup Resale Operations, and Solution Architect.
- Territory, team, effective start/end, and notes.
- Current project and intake AE/SA signal cards.
- Server structural validation, a 1,000-row limit, distinct primary/backup checks, and effective-date ordering.
- CSV draft export.

## Persistence boundary

The draft is intentionally unsaved. SAL-002 needs a new audited effective-dated alignment model; no database design or database change is authorized. The registered source route has no INSERT, UPDATE, DELETE, migration, or external write.

- Azure changed: no.
- Database changed: no.
- Entra changed: no.
- Commit, push, and deployment: not performed.
