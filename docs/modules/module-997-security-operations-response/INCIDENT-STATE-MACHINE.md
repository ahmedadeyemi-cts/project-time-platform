# Module 997 Incident State Machine

| Step | State | Source behavior | Production gate |
|---|---|---|---|
| 1 | Detect | Signal schema and ownership only | Approved telemetry connector |
| 2 | Triage | Severity, confidence, scope, and guidance | Human analyst review |
| 3 | Declare | Locked endpoint | Durable incident store and authority |
| 4 | Contain | Locked endpoint | Incident commander, separated approval, approved adapter |
| 5 | Eradicate | Locked endpoint | Confirmed cause and change control |
| 6 | Recover | Locked endpoint | Business-owner verification and recovery plan |
| 7 | Review | Guidance and reporting policy | Evidence and stakeholder review |
| 8 | Close | Locked endpoint | Closure approval and retention transition |

No lifecycle transition is persisted. All mutation-shaped routes authorize the
actual session and return HTTP 423 before body processing.
