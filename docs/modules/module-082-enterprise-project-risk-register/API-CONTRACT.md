# Module 082 API contract

| Method | Route | Purpose |
|---|---|---|
| GET | `/summary` | Scoped risk and action KPIs |
| GET | `/projects` | Authoritative accessible projects |
| GET | `/directory/users?projectId=` | Active, eligible project owners |
| GET/POST | `/risks` | Filter or identify risks |
| PUT | `/risks/{id}` | Revisioned reassessment with change reason |
| POST | `/risks/{id}/close` | Evidence-backed governed closure |
| POST | `/risks/{id}/realize` | Evidence-backed issue realization |
| GET | `/heatmap` | Scoped 5×5 inherent-exposure cells |
| GET | `/actions` | Scoped and assigned response actions |
| POST | `/risks/{id}/actions` | Create response action |
| PUT | `/actions/{id}` | Owner/manager revisioned action update |
| GET | `/review-calendar` | Governed review cadence window |
| GET | `/history` | Immutable scoped audit evidence |
| GET | `/exports/{xlsx\|pdf}` | Branded, role-scoped evidence export |

Exposure is computed at the database boundary as probability multiplied by the greatest impact dimension. Closed and retired risks are immutable. Every change produces a version snapshot and an audit event; action changes produce action history and audit evidence.
