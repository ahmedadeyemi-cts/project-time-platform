# Module 067 Security and Operations

## Secret boundary

Secret values remain server-side. The API may return only:

- whether an approved environment source is configured;
- the environment-variable name that supplied it; and
- a short SHA-256 fingerprint for operator comparison.

The API does not return the secret, length, prefix, suffix, token payload,
certificate bytes, connection string, or raw provider response.

## Provider migration gate

Microsoft Graph is the preferred target. Exchange Online SMTP/OAuth is the
approved alternate. Password-only SMTP is not approved. A production cutover
must show:

1. tenant, client, approved credential, sender, and recipient boundary;
2. authorized Send As / Send on Behalf behavior and Reply-To;
3. test-only connectivity and single-recipient evidence;
4. SPF, DKIM, DMARC, and accepted-domain readiness;
5. idempotent outbox, retry, throttling, dead-letter, and audit behavior;
6. every existing consumer using the shared abstraction;
7. Brevo disabled and removed from active production configuration; and
8. an authorized rollback plan.

## Logging

Authorization failures and dependency errors are logged by exception type only.
No raw exception message or configuration value is placed in the response.

## Operational status

This package is source-only, uncommitted, unpushed, and undeployed. It performs
no Azure, Entra, database, secret-store, DNS, provider, or delivery action.
