# Module 081 API contract

All operational endpoints resolve the actual ProjectPulse session, the effective View-As identity, permission codes, team scope, and project scope on the server. View-As is read-only and cannot import or export.

| Method | Route | Purpose |
|---|---|---|
| GET | `/summary` | Scoped KPIs and effective permissions |
| GET/POST | `/equipment` | List or create equipment |
| PUT | `/equipment/{id}` | Revision-guarded equipment update |
| POST | `/equipment/{id}/retire` | Governed retirement or disposal |
| GET/POST | `/ip-addresses` | List or create IP allocations |
| PUT | `/ip-addresses/{id}` | Revision-guarded IP update |
| GET/POST | `/connections` | List or create cabling connections |
| GET | `/rack-view` | 42U rack occupancy and conflict evidence |
| GET | `/imports` | Administrator import history |
| POST | `/imports/preview` | Non-destructive CSV/XLSX preview |
| POST | `/imports/{id}/commit` | Commit accepted/reviewed rows |
| DELETE | `/imports/{id}/preview` | Cancel an uncommitted preview |
| GET | `/history` | Immutable, record-scoped audit events |
| GET | `/exports/{xlsx\|pdf}` | Branded, role-scoped evidence export |

Mutations use allowlisted lifecycle values, typed PostgreSQL parameters, optimistic revisions, network/rack validation triggers, and append-only audit events. Duplicate IPs, overlapping networks, repeated cable endpoints, serial/asset collisions, and rack overlaps fail closed.
