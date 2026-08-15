# Module 072 Authorization Matrix

| Capability | Manager | Engineering Lead | Project Team Coordinator | Platform Administrator | Other authenticated users | Public client |
|---|---:|---:|---:|---:|---:|---:|
| View customer name and PIN | Yes | Yes | Yes | Yes | Yes | No |
| Search name, PIN, and ID | Yes | Yes | Yes | Yes | Yes | No |
| Download visible directory | Yes | Yes | Yes | Yes | Yes | No |
| Add/edit/remove routes | Yes | Yes | Yes | Yes | No | No |
| Import CSV/XLSX preview | Yes | Yes | Yes | Yes | No | No |
| Save directory | Yes | Yes | Yes | Yes | No | No |

## Enforcement rules

- All reads require an authenticated Pulse session.
- Management accepts canonical `ENGINEERING_LEAD`, compatibility `ENGINEERING_TEAM_LEAD`, `MANAGER`, `PROJECT_TEAM_COORDINATOR`, `SUPER_ADMINISTRATOR`, and legacy `ADMINISTRATOR`.
- View-As never transfers management authority.
- Frontend visibility mirrors the backend result and does not grant authority.
- Every successful save records the actual user ID and immutable revision evidence.
- PINs are internal routing identifiers and must not be treated as authentication credentials.
