# Module 065 Approved Adapter Contract

`IEntraSecretRotationAdapter` is the sole credential mutation boundary. The source ships only `LockedEntraSecretRotationAdapter`.

## Methods

- `PrepareAsync` records a non-secret plan.
- `ApproveAsync` records an explicit approve/reject decision.
- `StageSecretAsync` receives a `SensitiveSecretLease`; it must not stringify or log it.
- `TestAsync` performs a provider test and returns sanitized state only.
- `ActivateAsync` explicitly switches the active version and starts overlap.
- `RollbackAsync` restores a governed previous version.

## Result constraint

`EntraSecretOperationResult` can return only:

- success/status/message;
- operation ID;
- lifecycle state;
- non-sensitive version identifier;
- correlation ID;
- recorded timestamp.

There is no result property for secret values, tokens, provider payloads, exception text, connection strings, or secret-store references.

## Locked default

The default adapter reports `IsConfigured=false`, has adapter code `locked_no_external_adapter`, performs no work, and returns `external_authorization_required` for every method. Registering the Module 065 endpoints alone cannot mutate Azure, Entra, a database, or a secret store.
