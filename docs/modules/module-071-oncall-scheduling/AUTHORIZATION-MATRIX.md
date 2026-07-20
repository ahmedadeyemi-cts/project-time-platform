# Module 071 Authorization Matrix

| Capability | Manager | Engineering Team Lead | Administrator | Project Team Coordinator | All other authenticated users | Public client |
|---|---:|---:|---:|---:|---:|---:|
| View schedule | Yes | Yes | Yes | Yes | Yes | Through public GET API |
| View roster | Yes | Yes | Yes | Yes | Yes | No |
| View history | Yes | Yes | Yes | Yes | Yes | No |
| Add/edit/delete schedule entries | Yes | Yes | No | No | No | No |
| Change dates and identities | Yes | Yes | No | No | No | No |
| Manage rotation roster | Yes | Yes | No | No | No | No |
| Auto-generate schedule preview | Yes | Yes | No | No | No | No |
| Restore schedule history | Yes | Yes | No | No | No | No |

## Enforcement rules

- Management authorization is calculated from the actual ProjectPulse user, never the View-As identity.
- Only exact canonical role codes `MANAGER` and `ENGINEERING_TEAM_LEAD` grant management authority.
- `ADMINISTRATOR`, `SUPER_ADMINISTRATOR`, `SYSTEM_ADMINISTRATION`, and `MANAGE_ALL` do not implicitly grant Module 071 management authority.
- Frontend controls reflect the server result but never replace backend enforcement.
- The governed permission label is `MANAGE_ONCALL_SCHEDULE`.

## MODULES_064_074_PLATFORM_ADMIN_ALIGNMENT

Source checkpoint: `3d9a3dca8af479c854dc4c4a9294bc8aad273074`

Module 071 management authority includes `SUPER_ADMINISTRATOR`, `ADMINISTRATOR`, `MANAGER`, and `ENGINEERING_TEAM_LEAD`. Authority is resolved from the actual ProjectPulse session. View-As remains read-only and cannot transfer schedule, roster, generation, or history-restore authority.

## PROJECTPULSE_NATIVE_POSTGRESQL_MIGRATION_031

- Source parent: `603538ad408b70b3e6a26ff2f4f162599fa1cabf`
- Migration source: `database/migrations/031_modules_071_072_native_persistence.sql`
- Rollback source: `database/rollback/031_modules_071_072_native_persistence_rollback.sql`
- Module 071 persistence: ProjectPulse PostgreSQL schedule, roster, acknowledgement, and history tables
- Module 072 persistence: ProjectPulse PostgreSQL routing directory and immutable revision tables
- Platform Administrator authority: explicit
- View-As write authority: blocked
- External compatibility runtime dependency: removed
- Migration applied: no
- Database changed: no
- Deployment performed: no
