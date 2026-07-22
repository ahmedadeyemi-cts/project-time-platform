# Module 026 API contract

All non-public endpoints require a valid ProjectPulse session. Mutation routes
also require same-origin requests, actual-session authority, and
`MANAGE_INTEGRATIONS_026` or an authorized administrator role.

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/integrations/026/providers` | Sanitized provider configuration and availability |
| `POST` | `/api/integrations/026/providers` | Add a manual CRM/ERP provider |
| `PUT` | `/api/integrations/026/providers/{providerKey}` | Update non-secret connection metadata |
| `PUT` | `/api/integrations/026/providers/{providerKey}/credential` | Replace a write-only API key or OAuth client secret |
| `POST` | `/api/integrations/026/providers/{providerKey}/oauth/start` | Create a one-time OAuth state and authorization URL |
| `GET` | `/api/public/integrations/026/oauth/callback` | Consume the one-time OAuth callback and store encrypted tokens |
| `POST` | `/api/integrations/026/providers/{providerKey}/test` | Run and audit an explicit availability test |

Credential and token values never appear in a response. The public callback is
authorized by a 256-bit, single-use, hashed state value with a ten-minute
expiry; it does not accept arbitrary provider or actor identifiers.

Only public HTTPS provider endpoints are accepted. Connection tests do not
follow redirects or store response bodies.
