# Module 998 API Contract

Contract version: `2026-07-20.1`

## Read endpoints

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/system-diagnostics/overview` | Sanitized module posture, access, categories, ownership, and guardrails |
| GET | `/api/system-diagnostics/checks` | Direct, delegated, governed, and unknown diagnostic checks |
| GET | `/api/system-diagnostics/issues` | Non-persistent issue-classification contract; no live telemetry claim |
| GET | `/api/system-diagnostics/evidence-policy` | Evidence metadata, redaction, chain-of-custody, and exclusion policy |
| GET | `/api/system-diagnostics/remediation-policy` | Controlled lifecycle and separation-of-duties contract |
| GET | `/api/system-diagnostics/runbooks` | Guidance-only diagnostic runbooks and owning-module links |

## Locked operation endpoints

| Method | Path | Locked outcome |
|---|---|---|
| POST | `/api/system-diagnostics/analysis` | AI execution locked |
| POST | `/api/system-diagnostics/remediation/prepare` | Proposal creation locked |
| POST | `/api/system-diagnostics/remediation/approve` | Approval persistence locked |
| POST | `/api/system-diagnostics/remediation/stage` | Staged execution locked |
| POST | `/api/system-diagnostics/remediation/promote` | Production execution locked |
| POST | `/api/system-diagnostics/remediation/verify` | Post-action verification locked |
| POST | `/api/system-diagnostics/remediation/rollback` | Rollback execution locked |
| POST | `/api/system-diagnostics/remediation/close` | Closure and retention transition locked |

Every operation endpoint authenticates and authorizes the actual session, then
returns HTTP `423 Locked`. It does not read the request body, invoke an adapter,
write a record, access a secret, call AI, send a notification, contain a threat,
promote a deployment, or execute rollback.

## Common failures

- `401 session_required` — no actual ProjectPulse session.
- `403 diagnostic_access_required` — actual session lacks server-side access.
- `423 operation_locked` — lifecycle source exists but execution is disabled.
- `503 authorization_dependency_unavailable` — the authorization dependency is
  missing or unavailable; raw failure details are suppressed.

## Data rules

Responses may contain logical module ownership, sanitized status, timestamps,
contract versions, severity definitions, and redacted evidence rules. Responses
must not contain raw logs, stack traces, private topology, tenant IDs,
credentials, tokens, connection strings, secret values, or unredacted customer
or user data.
