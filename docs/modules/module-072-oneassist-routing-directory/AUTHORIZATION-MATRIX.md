# Module 072 Authorization Matrix

| Capability | Manager | Administrator / Super Administrator | Project Team Coordinator | Solution Architect | Other authenticated users | Public client |
|---|---:|---:|---:|---:|---:|---:|
| View customer name and PIN | Yes | Yes | Yes | Yes | Yes | Yes |
| Search name, PIN, and ID | Yes | Yes | Yes | Yes | Yes | Through GET API |
| Download visible directory | Yes | Yes | Yes | Yes | Yes | Through GET API |
| Add/edit/remove routes | Yes | Yes | Yes | No | No | No |
| Import CSV/XLSX preview | Yes | Yes | Yes | No | No | No |
| Save directory | Yes | Yes | Yes | No | No | No |

## Enforcement rules

- Management uses exact canonical role codes `MANAGER`, `ADMINISTRATOR`, `SUPER_ADMINISTRATOR`, and `PROJECT_TEAM_COORDINATOR`.
- Frontend visibility mirrors the backend result but does not grant authority.
- View-As never transfers management authority.
- Every successful save records the actual user ID and requires downstream audit evidence.
- Routing PINs are public identifiers and must not be used as authentication credentials.

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
