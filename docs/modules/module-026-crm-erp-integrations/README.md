# Module 026 — CRM/ERP Integration Control Center

Module 026 replaces the historical browser-local CRM framework overlay with a
native ProjectPulse page and backend API. It owns integration configuration and
sanitized availability for:

- SELL (Zendesk Sell)
- Salesforce
- Certinia
- ServiceNow
- manually registered CRM, ERP, PSA, ITSM, or related platforms

## Authentication

Each provider uses one explicit authentication model:

- OAuth 2.0 authorization code flow; or
- a write-only API key with an administrator-configured header and prefix.

OAuth client secrets, API keys, access tokens, and refresh tokens are encrypted
with AES-256-GCM using `PROJECTPULSE_INTEGRATION_SECRET_ENCRYPTION_KEY`. Secret
values are never returned by Module 026 APIs, displayed after saving, written to
logs, stored in Git, or included in audit evidence.

Certinia uses an approved Salesforce connected-app/OAuth boundary. Tenant- or
instance-specific endpoints are administrator configuration; the repository
does not guess an organization URL.

## Availability status

An Integration Administrator or Administrator runs an explicit connection
test against the provider's configured public HTTPS health/resource endpoint.
Module 026 records only:

- `available`
- `authentication_failed`
- `unavailable`
- HTTP status when present
- duration
- a sanitized error code
- actor and timestamp

The service is `not_configured` until configuration, credentials, and an
explicit test are complete. Redirects are not followed, private/link-local
targets are rejected, and status response bodies are not stored.

## Database boundary

Migration `034_module_026_crm_erp_integrations.sql` adds provider metadata,
encrypted credential storage, one-time OAuth state, connection-check evidence,
permissions, and the built-in provider records. Creating this migration does
not authorize applying it. Database application and production deployment
remain separate governed actions.

## UI access

Sales, Account Executive, Inside Sales, Solution Architect, and Project Team
Coordinator roles can be granted sanitized status visibility. Provider
configuration, manual registration, credentials, OAuth, and tests require
`MANAGE_INTEGRATIONS_026` or an authorized Administrator/Integration
Administrator role. View-As never transfers mutation authority.
