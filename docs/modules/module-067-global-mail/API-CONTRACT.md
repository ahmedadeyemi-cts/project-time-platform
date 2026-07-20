# Module 067 API Contract

## Common controls

Both endpoints:

- accept `GET` only;
- require a valid ProjectPulse session;
- authorize the actual session as Administrator/Super Administrator or through
  `SYSTEM_ADMINISTRATION` / `MANAGE_ALL`;
- never use View-As to transfer authority;
- perform no mail-provider request and no database mutation; and
- return sanitized failures without secret values or raw exceptions.

| HTTP | Status | Meaning |
|---|---|---|
| 401 | `session_required` | No valid ProjectPulse session. |
| 403 | `administrator_access_required` | Actual session lacks authority. |
| 503 | `authorization_dependency_unavailable` | Authorization could not fail safely. |

## `GET /api/global-mail/configuration`

Returns:

- the normalized active provider and approved Microsoft 365 targets;
- fixed Graph/Exchange endpoints and non-secret timeout/retry settings;
- masked tenant/client identifiers;
- configured sender, Reply-To, and recipient-environment metadata;
- secret presence, source-name, and twelve-character SHA-256 fingerprint only;
- legacy-provider migration state;
- the consumer registry and ownership state; and
- immutable flags proving send, rotation, activation, and mutation are disabled.

No secret value is serialized.

## `GET /api/global-mail/health`

Evaluates configuration presence only. It returns required migration checks,
blocking count, and overall readiness for a future controlled connectivity
test. It always reports `providerRequestAttempted=false` and `messageSent=false`.

## Future mutation contract

Future secret rotation, activation, rollback, and test-delivery endpoints are
not included. They require step-up authentication, an approved secret store,
immutable sanitized audit evidence, recipient safety, idempotency, and explicit
Azure/Entra/deployment authorization before their API contract can be approved.
