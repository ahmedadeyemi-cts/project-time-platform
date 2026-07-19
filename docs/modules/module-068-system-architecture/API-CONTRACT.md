# Module 068 API Contract

## Common controls

Both endpoints:

- accept `GET` only;
- require a valid ProjectPulse session;
- authorize `SUPER_ADMINISTRATOR`, `ADMINISTRATOR`, `SYSTEM_ADMINISTRATION`, or
  `MANAGE_ALL` using the actual session user;
- do not grant access from the effective View-As identity;
- return sanitized failures without configuration values or raw exceptions;
- perform no database mutation.

Common failures:

| HTTP | Status | Meaning |
|---|---|---|
| 401 | `session_required` | No valid ProjectPulse session is available. |
| 403 | `administrator_access_required` | The actual session lacks administrative authority. |
| 503 | `authorization_dependency_unavailable` | Authorization cannot be completed safely. |

## `GET /api/system-architecture/overview`

Returns the versioned logical architecture contract.

Top-level fields:

| Field | Purpose |
|---|---|
| `module`, `moduleName`, `status` | Stable Module 068 identity and response state. |
| `contractVersion` | Diagram/API contract version. |
| `implementationBaseline` | Governed source baseline used for this package. |
| `runtimeRevision` | Sanitized runtime-supplied source revision or `runtime_managed`. |
| `generatedAt` | UTC response timestamp. |
| `access` | Server authorization and View-As boundary metadata. |
| `scope` | Logical diagram inclusion and exclusion rules. |
| `layers` | Ordered architecture layers. |
| `nodes` | Logical component registry. |
| `connections` | Protocol, data-purpose, classification, and direction registry. |
| `trustBoundaries` | Security boundary ownership and controls. |
| `environments` | Local, controlled-test, and production promotion path. |
| `statusLinks` | Existing routes and APIs that own live health. |
| `guardrails` | Non-mutation, secrecy, authority, and preservation rules. |

The response contains logical names only. It intentionally excludes physical
host names, tenant IDs, IP addresses, connection strings, and secret metadata.

## `GET /api/system-architecture/dependency-status`

Returns a role-safe dependency registry.

Direct observations are limited to:

- the authenticated request accepted by ProjectPulse middleware; and
- the PostgreSQL authorization query plus a read-only `SELECT 1` check.

All other dependencies have `delegated` or `governed` state and point to the
existing module that owns live health. Module 068 never treats a configured or
delegated integration as healthy without evidence from its owner.

Top-level fields:

| Field | Purpose |
|---|---|
| `status` | `dependency_status_loaded` on success. |
| `contractVersion` | Contract version shared with the overview. |
| `observedAt` | UTC observation time. |
| `observationMode` | `safe_local_and_delegated_health`. |
| `environment` | Sanitized environment class. |
| `dependencies` | Direct, delegated, and governed dependency rows. |
| `rules` | Interpretation and secrecy rules. |

## Mutation inventory

Module 068 defines no `POST`, `PUT`, `PATCH`, or `DELETE` route. It contains no
mutation SQL and no client-side write request.
