# Module 059 — Global Session Intelligence Visibility

## Purpose

Module 059 provides the **US Signal Session Intelligence** handle and drawer for
authenticated ProjectPulse users. It displays sanitized session, identity,
authorization, device, network, client-environment, deployment, privacy, and
diagnostic context.

## Global visibility requirement

Module 059 must remain mounted on every authenticated ProjectPulse route, including
independently isolated pages such as:

- `#opportunities`
- `#user-guide`
- `#calendar-capacity`
- `#cicd-pipeline`
- `#contracts`

## Root cause corrected

The Session Intelligence drawer was mounted inside the legacy Module 057 structural
route boundary. Routes excluded from that legacy boundary correctly avoided the
large Dashboard/Timesheet content block, but they also unintentionally excluded
Module 059.

The drawer is now mounted after the Module 060 non-contract route boundary and
immediately before the global Help assistant. This places it in the global
authenticated page shell rather than in any individual module route.

## Implementation marker

`MODULE_059_GLOBAL_ROUTE_HOST`

The host also contains:

- `data-module="059"`
- `data-route-scope="all-authenticated-pages"`

## Deployment status

**Status:** Complete — source committed, web deployed, and technical validation passed.

**Confirmed:** 2026-07-18 UTC

### GitHub checkpoints

- Branch: `fix/module-059-global-visibility-20260718`
- Global-visibility implementation commit: `ff9c6d285407aa5db8c14f00a17508982183c185`
- Deployment documentation commits: `1603120d0182ae243304b963b41d8445bff6453f`, `ac59c412c50b8a7291abaa3cda2c1fa9d6a3caef`, `01d9a068503082da4cbb8b7e9b2798da7f857b75`

### Azure runtime

- API image: `acrphdtest7825cc.azurecr.io/project-health-dashboard-api@sha256:10185bc58252c768577a343b734a80221ed5949d1b7ad141643bc90556dc43f4`
- API revision: `ca-phd-test-api-westus3--m063api4-0717232631`
- Web image: `acrphdtest7825cc.azurecr.io/project-health-dashboard-web@sha256:b3851f21eb235c0bcf2a25cbdf88200ee7e4d12f370fb1ffbd303e3697420768`
- Web revision: `ca-phd-test-web-westus3--m059g1-0718015727`

### Validation evidence

- Frontend build: passed.
- Module 059 mount count: `1`.
- Global route host marker: present.
- Route scope: all authenticated pages.
- Module 063 included: yes.
- Module 999 included: yes.
- Contracts included: yes.
- Calendar Capacity included: yes.
- CI/CD Pipeline included: yes.
- Public root: HTTP `200`.
- Public health: HTTP `200`.
- Unauthenticated Module 063 access endpoint: HTTP `401`.
- Global host bundle markers: passed.
- Preserved-module markers: passed.
- Rollback attempted: no.
- Rollback result: `not-required`.

## Change boundaries

This correction is frontend-only.

- API changed: No
- Database changed: No
- Entra changed: No

## Deployment evidence directory

`/home/ahmed/az12d4/module-059-global-20260718T015727Z`

## Permanent global-module contract

Module 059 is global authenticated application chrome, not a page owned by any
individual module. It must appear consistently on every current and future
ProjectPulse module numbered from Module 001 through Module 999.

The frontend build now runs:

`npm run validate:module059`

The validation fails the build when any of these invariants are broken:

- Module 059 is imported or mounted more than once.
- The global host marker, class, or all-authenticated-pages scope is removed.
- The drawer is moved back inside a route, role, or permission condition.
- The drawer is no longer after route-specific content and before global Help.
- The Session Intelligence handle is removed, hidden, or changed from fixed
  global positioning.
- The installed route registry cannot be enumerated.

New module routes automatically inherit Module 059 by remaining inside the
authenticated App shell. A future module must not create a separate application
shell that bypasses the global host.
