# Module 065 Security Boundary

## Prohibited paths

Usable secret values, access tokens, refresh tokens, authorization codes, and raw provider payloads must never appear in:

- frontend state or browser storage;
- URLs, query strings, or request headers;
- API responses;
- application, reverse-proxy, platform, or provider logs;
- traces, metrics, exception detail, or support bundles;
- audit records or exports;
- source control, CI artifacts, screenshots, or documentation.

The Module 065 frontend contains no secret field and issues only GET requests. The specialized future client must send a raw write-only body directly to the same-origin API.

## Fail-closed order

Before a mutation body is read, the backend checks:

1. valid actual and effective ProjectPulse session identity;
2. actual `SUPER_ADMINISTRATOR` role or `MANAGE_ENTRA_SECRET` capability;
3. View-As is not active;
4. explicit mutation switch;
5. recorded external Azure/Entra authorization;
6. injected approved adapter;
7. recent server-established step-up context.

The default locked adapter returns no success state and never dereferences the secret lease.

## Adapter acceptance criteria

Any future adapter must receive a separate security and governance review proving:

- approved encrypted secret storage with access policy and versioning;
- no secret material in structured logging, exceptions, telemetry, or diagnostic scopes;
- atomic state transition and append-only audit behavior;
- initiating actor cannot self-approve when two-person control applies;
- token tests return a category and timestamp, never token/provider detail;
- activation is explicit and cannot precede successful validation;
- overlap is bounded and the prior version remains recoverable;
- rollback selects a governed prior version without accepting arbitrary secret input;
- rate limiting, idempotency, request correlation, replay protection, and concurrency control;
- memory buffers are cleared and no request body is retained.

No Azure, Entra, database, secret-store, or deployment mutation is performed by this source package.
