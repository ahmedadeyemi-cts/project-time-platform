# Module 068 Capability Matrix

| Requirement | Priority | Package outcome | Evidence | Runtime gate |
|---|---:|---|---|---|
| OPS-013 — versioned System Architecture page | P0 | Implemented | Contract version, logical layers, nodes, connections, trust boundaries, and environment path | Merge, deploy, and portal verify |
| OPS-013 — component communication | P0 | Implemented | Versioned connection registry | Portal visual review |
| OPS-013 — data communication | P0 | Implemented | Data purpose and classification per connection | API response review |
| OPS-013 — authentication communication | P0 | Implemented | Session/API/identity nodes and trust boundaries | Role-negative tests |
| OPS-013 — integration communication | P0 | Implemented | Identity, business integrations, shared platform services, and delivery pipeline | Owner health review |
| OPS-013 — environment communication | P0 | Implemented | Local, controlled test, and production promotion stages | Deployment evidence |
| OPS-013 — role-safe | P0 | Implemented | Actual-session backend admin authorization; frontend admin route | 401/403/View-As smoke tests |
| OPS-013 — live status links | P0 | Implemented | Modules 010, 013–017, and 058 route/API ownership links | Portal link smoke tests |
| OPS-013 — no secret exposure | P0 | Implemented | Logical-only response, sanitized errors, validator guards | Response and source scan |
| OPS-015 — operational runbook ownership | P1 | Foundation | Module-owner mapping and post-deployment verification runbook | Add owner/last-validation metadata in future OPS-015 work |
| Module 059 global preservation | P0 | Guarded | Existing global validator remains in build chain | Module 059 validation |
| Module 062 preservation | P0 | Guarded | Existing Module 062 validator remains in build chain | Module 062 validation |
| Module 002 preservation | P0 | Release-train gate | Package is replayed after Module 002 merge and remains uncommitted | Run Module 002 preservation validator on the exact current-main train |
| Database schema | — | Unchanged | Read-only authorization and `SELECT 1`; no migration | None |
| Azure / Entra | — | Unchanged | No resource or configuration mutation | None |
| Deployment | — | Not performed | Source package only | Explicit authorization required |

## Completion labels

- `Implemented` means the full source behavior exists in the isolated package.
- `Guarded` means an existing validation contract remains part of the build.
- `Integration hold` means the source is intentionally not committed or pushed
  while Module 002 owns overlapping shared files.
- No row claims runtime activation or production acceptance.
