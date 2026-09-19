# Module 026 API contract

All non-public endpoints require a valid ProjectPulse session. Mutation routes
also require same-origin requests, actual-session authority, and
`MANAGE_INTEGRATIONS_026` or an authorized administrator role.

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/integrations/026/providers` | Sanitized provider configuration and availability |
| `POST` | `/api/integrations/026/providers` | Add a manual CRM/ERP provider |
| `PUT` | `/api/integrations/026/providers/{providerKey}` | Update non-secret connection, record-lookup-template, and import-mapping metadata |
| `PUT` | `/api/integrations/026/providers/{providerKey}/credential` | Replace a write-only API key or OAuth client secret |
| `POST` | `/api/integrations/026/providers/{providerKey}/oauth/start` | Create a one-time OAuth state and authorization URL |
| `GET` | `/api/public/integrations/026/oauth/callback` | Consume the one-time OAuth callback and store encrypted tokens |
| `POST` | `/api/integrations/026/providers/{providerKey}/test` | Run and audit an explicit availability test |

Credential and token values never appear in a response. The public callback is
authorized by a 256-bit, single-use, hashed state value with a ten-minute
expiry; it does not accept arbitrary provider or actor identifiers.

Only public HTTPS provider endpoints are accepted. Connection tests do not
follow redirects or store response bodies.

Module 055D is an internal consumer of the configured ConnectWise SELL provider. Its
`POST /api/work-register/intake/packages/sell/import` route accepts a ConnectWise SELL
record ID, replaces the required `{recordId}` lookup-template token, applies
the administrator-owned mapping, and retains only the mapped Work Register
fields.

## ConnectWise SELL credentials

For `connectwise_sell`, `PUT /api/integrations/026/providers/connectwise_sell/credential`
accepts `accessKey`, `publicKey`, and `privateKey`. All three are required together;
`secret` is reserved for other providers. The three values are encrypted as one
credential and never returned. Configuration saves do not require credentials.
The built-in API origin, read-only quote test, Authorization header and Basic
prefix are validated server-side; OAuth is rejected.

See [setup and API boundaries](CONNECTWISE-SELL-SETUP.md). Migration 111 adds the
new provider identity while preserving old source and submission evidence.
