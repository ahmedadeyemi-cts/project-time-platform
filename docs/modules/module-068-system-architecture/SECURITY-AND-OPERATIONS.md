# Module 068 Security and Operations

## Authorization model

The global ProjectPulse session middleware validates authentication before the
Module 068 handlers run. Module 068 then resolves authority in this order:

1. `ProjectPulseActualUserId` when View-As is active.
2. `ProjectPulseSessionUserId` for a normal authenticated session.
3. Fail closed when neither is present.

It intentionally ignores `ProjectPulseEffectiveUserId` for privileged authority.
An administrator may view the architecture while previewing another user, but
the viewed user never supplies or inherits the permission decision.

Server authorization requires an active `SUPER_ADMINISTRATOR` or
`ADMINISTRATOR` role, or an active role granting `SYSTEM_ADMINISTRATION` or
`MANAGE_ALL`.

## Information exposure controls

The API permits:

- logical component and layer names;
- general protocols and data classifications;
- module route and protected API ownership;
- safe environment classes;
- contract, baseline, and sanitized runtime revision identifiers;
- direct database/session observation and delegated status labels.

The API excludes:

- database, provider, SMTP, AI, Graph, Azure, or Entra secret values;
- connection strings, usernames, private host names, IP addresses, subscription
  details, tenant IDs, and application IDs;
- raw provider payloads and raw exception messages;
- discovered network topology;
- customer, user, project, time, billing, or approval records.

Authorization dependency failures are logged only by exception type and return a
generic 503 response.

## Operational ownership

Module 068 is an index and explanation layer. It does not duplicate the live
checks owned by:

| Owner | Evidence |
|---|---|
| Module 010 | Azure/Entra configuration and identity readiness |
| Module 013 | Service, API, and runtime version status |
| Modules 014–016 | Backup, retention, and restore evidence |
| Module 017 | Replication and synchronization status |
| Module 058 | Source, build, deployment, and rollback status |

Links remain inside the authenticated ProjectPulse shell. Status API paths are
shown as ownership evidence; the browser does not automatically fan out to every
endpoint.

## Failure behavior

- Missing session: 401.
- Insufficient actual-session authority: 403.
- Missing/unavailable authorization database: sanitized 503.
- Frontend request failure: local route error banner; no raw response body.
- Delegated health unavailable: remains delegated, never promoted to healthy.

## Operational verification after deployment

A separately authorized deployment must verify:

1. Administrator navigation and page rendering.
2. Non-administrator route suppression and backend 403 behavior.
3. View-As does not transfer architecture authority.
4. Overview and dependency endpoints return no secret or physical-topology data.
5. Every live status link opens its existing protected module.
6. Module 059 remains mounted once after all authenticated route content.
7. Modules 001, 002, 056E, 062, 066A, and every recovered route remain intact.

No deployment or portal verification is part of the uncommitted source package.
