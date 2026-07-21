# Module 997 Integration Boundary

| Integration | Owner | State |
|---|---|---|
| ProjectPulse authentication and session telemetry | Module 997 | Connected |
| ProjectPulse incident and response store | Migration 033 / Module 997 | Operational activation |
| Controlled diagnostics | Module 998 | Connected through incident handoff |
| Native session revocation | Module 997 | Explicit runtime switch |
| Entra user containment | Identity adapter | Not configured |
| WAF/network blocking | Network security adapter | Not configured |
| Endpoint isolation | Endpoint security adapter | Not configured |
| Integration quarantine | Module 075 adapter | Not configured |
| AI analysis | Module 064 | Not authorized for security evidence |
| External notification | Module 067 | Not authorized for incident data |

Every external adapter requires least privilege, sanitized errors, health and
freshness evidence, rate limits, audit, rollback, and separate production
approval.
