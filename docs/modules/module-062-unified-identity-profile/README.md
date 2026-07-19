# Module 062 — Unified Identity Profile and Presence

## Purpose

Module 062 provides the shared identity, directory-profile, photograph, and
presence abstraction used by ProjectPulse modules.

It does not replace authentication, create a second session store, or introduce
another identity provider.

## Domain authority

| Domain | Authentication | Profile authority | Presence |
|---|---|---|---|
| `onenecklab.com` | Microsoft Entra test | Microsoft Graph with ProjectPulse fallback | Microsoft Graph |
| `ussignal.com` | Microsoft Entra production | Microsoft Graph with ProjectPulse fallback | Microsoft Graph |
| `ussignal.local` | ProjectPulse local | ProjectPulse local profile | Local/unavailable unless Microsoft-linked |
| `ussignal.cloud` | ProjectPulse local | ProjectPulse local profile | Local/unavailable unless Microsoft-linked |

## Shared profile contract

The shared profile includes:

- ProjectPulse user identifier
- Microsoft Entra object identifier when applicable
- email
- display name
- job title
- department
- team
- profile photograph
- authentication provider
- profile source
- directory provider
- normalized presence availability
- normalized presence activity
- display label
- CSS status family
- presence retrieval time
- presence support and error state

## Initial consumers

- Module 057 — Resource and Team Calendar Capacity
- Module 059 — Global Session Intelligence
- ProjectPulse Profile

Future modules must consume Module 062 rather than creating independent identity
and presence formatters.

## Presence normalization

Microsoft Graph values are normalized case-insensitively. Values such as
`Available`, `available`, `InAMeeting`, and `inAMeeting` resolve to one canonical
ProjectPulse identity-presence representation.

The UI must never derive the status color and status text through different
normalization paths.

## Change boundaries

- Authentication redesign: No
- New identity provider: No
- New session store: No
- Module 002 changes: No
- Azure changes: No
- Entra configuration changes in Phase 1: No
- Database changes in Phase 1: No

<!-- MODULE_062_PHASE_2_START -->
## Phase 2 — Unified current-user profile

Phase 2 adds:

- authenticated `GET /api/identity/profile`
- effective-user resolution from the existing ProjectPulse session
- Microsoft profile enrichment for `onenecklab.com`
- Microsoft profile enrichment for `ussignal.com`
- local profile authority for `ussignal.local`
- local profile authority for `ussignal.cloud`
- Microsoft Graph current-user presence
- case-insensitive shared presence normalization
- cached Microsoft profile photographs
- local database fallback when Graph is unavailable
- Module 059 consumption of Module 062 identity data

The endpoint does not replace authentication and does not create a second
session store.

### Supported credential selection

For test Microsoft identities, Module 062 checks:

- `PROJECTPULSE_ENTRA_TEST_TENANT_ID`
- `PROJECTPULSE_ENTRA_TEST_CLIENT_ID`
- `PROJECTPULSE_ENTRA_TEST_CLIENT_SECRET`

For production Microsoft identities, Module 062 checks:

- `PROJECTPULSE_ENTRA_PRODUCTION_TENANT_ID`
- `PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_ID`
- `PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_SECRET`

Both modes fall back to the existing generic
`PROJECTPULSE_ENTRA_TENANT_ID`, `PROJECTPULSE_ENTRA_CLIENT_ID`, and
`PROJECTPULSE_ENTRA_CLIENT_SECRET` configuration.

### Phase 2 boundaries

- Database schema changed: No
- Authentication redesigned: No
- New identity provider introduced: No
- Azure changed: No
- Entra configuration changed: No
- Module 002 changed: No
<!-- MODULE_062_PHASE_2_END -->

<!-- MODULE_062_PHASE_3_START -->
## Phase 3 — Profile consumer integration

The ProjectPulse Profile modal and top-right profile menu now consume Module
062.

Microsoft identities use Microsoft-backed values for:

- display name
- job title
- department
- profile photograph
- availability
- detailed presence activity

Local identities continue to use ProjectPulse profile values and preferences.

The following existing Profile capabilities remain in place:

- local profile-photo upload
- local display preference
- local title preference
- awards and certificates
- light and dark theme settings
- persistent settings storage
- session and workspace information

### Tenant selection safety

Explicit test and production Graph credentials are preferred.

Generic Graph credentials are used only when `PROJECTPULSE_ENTRA_MODE` matches
the identity domain:

- test, development, or onenecklab mode for `onenecklab.com`
- production, prod, or ussignal mode for `ussignal.com`

This prevents a generic test tenant configuration from being used to query a
production US Signal identity, and vice versa.

### Local-domain boundary

Existing `ussignal.local` and `ussignal.cloud` identities are resolved as local
ProjectPulse identities. Module 062 does not change user-provisioning policy or
the database trigger governing manual account creation.

### Final source boundaries

- Authentication redesign: No
- New session store: No
- Database schema change: No
- Database migration applied: No
- Azure change: No
- Entra configuration change: No
- Module 002 worktree change: No
<!-- MODULE_062_PHASE_3_END -->
