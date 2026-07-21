# Module 997 API Contract

Contract version: `2026-07-21.2`

## Read endpoints

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/security-operations/overview` | Live posture, native evidence counts, access, and adapter readiness |
| GET | `/api/security-operations/alerts` | Stored alerts and derived authentication signals |
| GET | `/api/security-operations/sessions` | Recent ProjectPulse sessions for correlated investigation |
| GET | `/api/security-operations/incidents` | Incident and containment queues |
| GET | `/api/security-operations/incidents/{id}` | Incident, timeline, and response evidence |
| GET | `/api/security-operations/threat-intelligence` | Native and external intelligence-source readiness |
| GET | `/api/security-operations/control-posture` | Native and delegated control evidence |
| GET | `/api/security-operations/response-policy` | Lifecycle and execution gates |
| GET | `/api/security-operations/reporting-policy` | Evidence, export, and notification boundaries |
| GET | `/api/security-operations/integration-policy` | Connector readiness and owners |

## Operational endpoints

| Method | Path | Outcome |
|---|---|---|
| POST | `/api/security-operations/incidents/declare` | Creates a durable incident and timeline event |
| POST | `/api/security-operations/incidents/acknowledge` | Assigns acknowledgement and owner evidence |
| POST | `/api/security-operations/response/contain` | Prepares a response request; never executes it |
| POST | `/api/security-operations/response/approve` | Records approval by a different eligible actor |
| POST | `/api/security-operations/response/execute` | Executes enabled native session revocation or returns exact adapter guidance |
| POST | `/api/security-operations/response/eradicate` | Records the eradication lifecycle state and evidence note |
| POST | `/api/security-operations/response/recover` | Records recovery lifecycle state and evidence note |
| POST | `/api/security-operations/case/close` | Closes only a recovered/reviewed incident with a summary |

AI analysis, external notification, and evidence export return `423
adapter_required`. Request bodies are size-limited and are read only after the
actual session, View-As, management permission, and schema gates pass.
