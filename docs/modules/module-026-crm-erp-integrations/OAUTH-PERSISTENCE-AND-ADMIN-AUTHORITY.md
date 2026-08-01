# Module 026 — Persistent OAuth Connections and Administrator Authority

## Administrator authority invariant

An actual ProjectPulse session with any of the following can manage Module 026:

- Super Administrator;
- Administrator;
- Integration Administrator;
- `MANAGE_ALL`; or
- `MANAGE_INTEGRATIONS_026`.

Administrator View-As remains read-only. After that boundary is enforced, an optional Module 012 dynamic policy can authorize additional delegated roles. A missing or not-configured optional policy can no longer reduce the actual Super Administrator’s authority.

## Persistent OAuth operation

OAuth providers save an encrypted envelope containing the access token, refresh token, provider instance URL, and access-token expiration time. The new persistence service renews enabled OAuth connections before the access token expires.

The service:

- evaluates connections every five minutes;
- begins renewal 15 minutes before expiration;
- uses a global PostgreSQL advisory lock so one application revision owns each evaluation cycle;
- uses a provider-specific advisory lock so two revisions cannot refresh the same connector simultaneously;
- validates the configured token endpoint as an approved public HTTPS endpoint;
- decrypts credentials only in server memory;
- uses the OAuth `refresh_token` grant;
- preserves the existing refresh token when a provider does not issue a replacement;
- re-encrypts the new access-token envelope immediately;
- clears temporary encryption-key bytes from memory;
- records append-only, sanitized refresh evidence; and
- never returns access tokens, refresh tokens, or client secrets to the browser.

A manual **Refresh OAuth token now** action uses the same guarded service. It is available only to an actual authorized Module 026 administrator.

## APIs

```text
GET  /api/integrations/026/token-refresh/status
POST /api/integrations/026/providers/{providerKey}/refresh-token
```

The status endpoint returns provider name, expiration time, renewal state, last refresh time, and sanitized diagnostic code. It returns no credential material.

## Evidence

Migration 056 creates:

```text
crm_integration_token_refresh_events
```

The table records the provider, manual/background trigger, sanitized status and diagnostic code, provider HTTP status, next expiration time, governed actor where available, and non-secret metadata. Rows are immutable.

## Browser session boundary

Persistent provider connectivity does not make a user’s ProjectPulse browser session permanent. ProjectPulse user sessions retain their existing expiration and extension controls. Provider-token renewal and user-session authentication are separate security boundaries.

## Activation boundary

The source package does not call a CRM provider during migration or deployment, change an existing credential, configure an OAuth client secret, deploy Test or Production, or apply migration 056. Provider calls occur only after the source is deployed, migration 056 is applied, a connector is enabled, and valid encrypted OAuth credentials already exist.
