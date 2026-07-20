# Module 065 — Entra Secret Administration

Module 065 is the governed application-credential lifecycle boundary for tracker requirement `RBAC-018`. It preserves ProjectPulse authentication and Module 010 Azure/Entra administration while adding privileged, non-secret credential visibility and complete fail-closed rotation contracts.

## Ownership boundary

- Module 010 owns tenant settings, directory preview/import, reconciliation, and user synchronization.
- Module 057 consumes identity and organization data.
- Module 062 owns the shared identity-profile abstraction.
- Module 065 owns application credential metadata, expiration health, rotation policy, safe lifecycle transitions, and sanitized audit contracts.

Module 065 does not create a new identity provider, login flow, session store, role system, or Microsoft Graph client.

## Implemented source

- Actual-session authorization for `SUPER_ADMINISTRATOR` or explicit `MANAGE_ENTRA_SECRET` delegation.
- Module 010 tenant/client metadata consumption with approved runtime-environment fallback.
- Application, environment, credential type, active version, non-sensitive fingerprint, last rotation, expiration, days remaining, and health.
- Critical expiration threshold at 14 days and warning threshold at 30 days.
- Complete prepare, dual-approval, write-only staging, token-test, activation, overlap, and rollback route contracts.
- Server-established five-minute step-up gate; browser headers cannot establish step-up authority.
- Raw `application/octet-stream` write-only transport with a 4 KiB bound and zeroed in-memory buffer.
- Locked default adapter that cannot store, test, activate, or roll back a credential.
- Sanitized response and append-only audit contracts that cannot return secrets or tokens.
- US Signal-branded, read-only operations center with no secret input control.

## Runtime status

The complete source is safe to register because mutation routes fail closed before reading a body unless all of these are true:

1. `PROJECTPULSE_ENTRA_SECRET_MUTATION_ENABLED=true`.
2. A non-secret external authorization record identifier is configured.
3. A reviewed `IEntraSecretRotationAdapter` is injected instead of the locked adapter.
4. The actual user is authorized and is not using View-As.
5. Server middleware established a recent step-up context.

This source does not include the external adapter, step-up middleware, approval persistence, audit persistence, secret-store configuration, or Azure/Entra authorization. It therefore performs no durable credential operation. The read-only center and fail-closed route contracts are registered in the current-main release train, but the locked default adapter prevents external secret operations.

## Non-secret runtime metadata names

- `PROJECTPULSE_ENTRA_APPLICATION_NAME`
- `PROJECTPULSE_ENTRA_MODE`
- `PROJECTPULSE_ENTRA_TENANT_ID`
- `PROJECTPULSE_ENTRA_CLIENT_ID`
- `PROJECTPULSE_ENTRA_SECRET_VERSION`
- `PROJECTPULSE_ENTRA_SECRET_FINGERPRINT`
- `PROJECTPULSE_ENTRA_SECRET_ROTATED_AT`
- `PROJECTPULSE_ENTRA_SECRET_EXPIRES_AT`
- `PROJECTPULSE_ENTRA_SECRET_STORE_REFERENCE` (presence only; never returned)
- `PROJECTPULSE_ENTRA_SECRET_STEP_UP_POLICY` (presence only)
- `PROJECTPULSE_ENTRA_SECRET_DUAL_APPROVAL_POLICY` (presence only)
- `PROJECTPULSE_ENTRA_SECRET_EXTERNAL_AUTHORIZATION_ID` (presence only; value never returned)
- `PROJECTPULSE_ENTRA_SECRET_MUTATION_ENABLED`

`PROJECTPULSE_ENTRA_CLIENT_SECRET` is used only to calculate a boolean configured signal. Its value is never returned, logged, fingerprinted, exported, or placed in an audit record by Module 065.

## External-state evidence

- Azure changed: no.
- Entra changed: no.
- Database changed: no.
- Deployment performed: no.
- Runtime registration integrated in source: yes, with the fail-closed adapter.
- Commit or push performed: no.
