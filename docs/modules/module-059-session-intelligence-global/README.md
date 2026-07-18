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

## Change boundaries

This correction is frontend-only.

- API change: No
- Database change: No
- Entra change: No
