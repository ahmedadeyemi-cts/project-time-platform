# Module 067 Capability Matrix

| Requirement | Priority | Package state | Evidence | Activation gate |
|---|---|---|---|---|
| OPS-016 secure Global Mail center | P0 | Implemented read-only | Backend, UI, API contract, validator | Semantic replay after Module 002 |
| CLS-005 Microsoft 365 migration | P0 | Governed readiness | Target/legacy state and migration checks | Provider and deployment authorization |
| Non-secret configuration view | P0 | Implemented | Configuration endpoint and UI | None after source activation |
| Write-only secret metadata | P0 | Implemented | Presence/source/fingerprint; no plaintext | Approved secret store for rotation |
| Actual-session administrator authority | P0 | Implemented | Server role/permission query | None after source activation |
| Provider connectivity validation | P0 | Locked | Health contract reports no provider request | Azure/Entra authorization |
| Test email | P0 | Locked | No POST/send endpoint | Recipient safety and delivery authorization |
| Staged rotation and rollback | P0 | Locked | Design documented; no mutation endpoint | Secret-store and audit authorization |
| Send As / Send on Behalf | P0 | Planned | Migration gate | Microsoft 365 permission approval |
| Shared consumer migration | P0 | Planned | Consumer registry | Separate reviewed consumer changes |
| Outbox/idempotency/retry/dead-letter | P0 | Required gate | Security and operations contract | Delivery design approval |
| SPF/DKIM/DMARC | P0 | External evidence required | Health check remains not observed | Domain-owner validation |

The source package does not claim runtime activation or complete Microsoft 365
cutover. It completes the safe application boundary available under the current
no-Azure/no-Entra/no-database/no-deployment authorization.
