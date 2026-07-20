# Module 998 Capability Matrix

| Tracker requirement | Priority | Source outcome | Runtime gate |
|---|---:|---|---|
| `GOV-016` diagnostic/remediation governance | P0 | Versioned ownership, authorization, evidence, and lifecycle contracts | Merge, deploy, portal acceptance |
| `OPS-005` unified diagnostic center | P0 | Safe overview and check registry implemented | Connect approved authoritative telemetry |
| `OPS-006` categorized operational issues | P0 | Sanitized severity and response model implemented | Durable approved issue source |
| `OPS-015` runbooks and ownership | P0 | Guidance-only runbooks link existing owners | Owner validation and operational acceptance |
| `OPS-017` controlled remediation | P0 | Prepare through close lifecycle modeled | Separate execution authorization and adapter |
| `OPS-018` evidence and redaction | P0 | Metadata, exclusions, and chain of custody modeled | Approved evidence store/export |
| `OPS-019` approval, staging, promotion, rollback | P0 | Every phase registered and fail-closed | Durable approvals and production authority |
| `OPS-020` operational control plane | P0 | Frontend/backend/source validator complete | Controlled deployment |
| `AI-020` diagnostic AI assistance | P1 | Sanitized endpoint contract registered and locked | Module 064 adapter review and explicit AI authority |
| `AI-021` security/operations AI boundary | P1 | Module 997 ownership boundary documented | Module 997 integration review |
| `DATA-011` diagnostic evidence model | P1 | Non-persistent versioned evidence contract | Database authorization and retention design |
| `RBAC-022` privileged operational access | P0 | Actual-session server authorization | Role-negative portal tests |
| Protected-module preservation | P0 | 002, 056E, 059, 062, and 064–074 remain in validator chain | Full source/build validation |

No row claims a live telemetry connector, AI execution, security containment,
external notification, production remediation, deployment, rollback, secret
access, or database persistence.
