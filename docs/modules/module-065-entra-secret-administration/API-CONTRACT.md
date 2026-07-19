# Module 065 API Contract

All routes use the actual ProjectPulse session. A canonical `SUPER_ADMINISTRATOR` or explicitly delegated `MANAGE_ENTRA_SECRET` capability is required. `ADMINISTRATOR`, `MANAGE_ALL`, and View-As do not confer Module 065 authority.

## Read routes

| Method | Route | Contract |
|---|---|---|
| `GET` | `/api/entra-secret-administration/capabilities` | Authority, ownership, mutation gates, adapter status, and secret boundary |
| `GET` | `/api/entra-secret-administration/metadata` | Non-secret credential metadata and expiration health |
| `GET` | `/api/entra-secret-administration/readiness` | Prerequisite checks without provider or secret-store calls |
| `GET` | `/api/entra-secret-administration/workflow-contract` | States, transitions, invariants, and write-only transport |
| `GET` | `/api/entra-secret-administration/audit-contract` | Required and prohibited sanitized audit fields |

## Guarded mutation routes

| Method | Route | Body | Purpose |
|---|---|---|---|
| `POST` | `/api/entra-secret-administration/rotations/prepare` | Non-secret JSON plan | Establish proposed version, expiration, overlap, approval policy, and reason |
| `POST` | `/api/entra-secret-administration/rotations/{operationId}/approve` | Non-secret JSON decision | Record the eligible second actor's decision |
| `PUT` | `/api/entra-secret-administration/rotations/{operationId}/secret` | Raw UTF-8 `application/octet-stream` | Send a write-only value to the approved adapter |
| `POST` | `/api/entra-secret-administration/rotations/{operationId}/test` | None | Run sanitized token acquisition through the approved adapter |
| `POST` | `/api/entra-secret-administration/rotations/{operationId}/activate` | None | Explicitly activate a validated version and begin overlap |
| `POST` | `/api/entra-secret-administration/rotations/{operationId}/rollback` | Non-secret JSON target/reason | Restore an approved previous version |

All mutation routes return `423 external_authorization_required` before reading a body when the external authorization, mutation switch, or approved adapter is missing. They return `428 recent_step_up_required` when server-established step-up context is missing or older than five minutes.

### Write-only secret transport

- `Content-Type`: `application/octet-stream`
- Required header: `X-ProjectPulse-Secret-Version`
- Maximum body size: 4096 bytes
- Cache headers: `no-store`, `no-cache`
- The body is read only after every external and authority gate passes.
- The in-memory byte buffer is zeroed after the adapter call.
- The response can contain only sanitized operation identity, state, version identifier, correlation, and timestamp.

The API never returns the usable secret, access token, refresh token, authorization code, secret-store reference, provider request/response, connection string, exception text, or adapter-supplied message. Adapter status, state, version, and correlation identifiers are restricted to bounded safe identifier characters before serialization.
