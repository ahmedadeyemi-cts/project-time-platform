# Module 997 API Contract

Contract version: `2026-07-20.1`

## Read endpoints

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/security-operations/overview` | Sanitized posture, access, severity, domains, ownership, and guardrails |
| GET | `/api/security-operations/alerts` | Empty connector-aware alert contract and required metadata |
| GET | `/api/security-operations/incidents` | Empty non-persistent incident contract, lifecycle, and objectives |
| GET | `/api/security-operations/threat-intelligence` | Source, confidence, freshness, and handling policy |
| GET | `/api/security-operations/control-posture` | Delegated and unknown control-evidence ownership |
| GET | `/api/security-operations/response-policy` | Incident lifecycle, locked gates, and separation of duties |
| GET | `/api/security-operations/reporting-policy` | Restricted reporting, audience, redaction, and export rules |
| GET | `/api/security-operations/integration-policy` | Explicit future adapters and disabled states |

## Locked operation endpoints

| Method | Path | Boundary |
|---|---|---|
| POST | `/api/security-operations/analysis` | AI security analysis locked |
| POST | `/api/security-operations/incidents/declare` | Durable incident declaration locked |
| POST | `/api/security-operations/incidents/acknowledge` | Incident acknowledgement locked |
| POST | `/api/security-operations/response/contain` | Containment locked |
| POST | `/api/security-operations/response/eradicate` | Eradication locked |
| POST | `/api/security-operations/response/recover` | Recovery execution locked |
| POST | `/api/security-operations/notifications/send` | External notification locked |
| POST | `/api/security-operations/evidence/export` | Evidence export locked |
| POST | `/api/security-operations/case/close` | Durable closure locked |

Every locked route returns HTTP `423 Locked` after authorizing the actual
ProjectPulse session. It does not read a request body, invoke an adapter, access
a secret, write state, or contact an external system.

## Common failures

- `401 session_required` — actual ProjectPulse session absent.
- `403 security_access_required` — actual session lacks server-side authority.
- `423 operation_locked` — operation exists as a discoverable contract only.
- `503 authorization_dependency_unavailable` — authorization dependency is
  unavailable; raw errors and provider details are suppressed.
