# 021B Navigation / Route Integrity

## Scope

021B adds a static route-integrity report for release hardening and demo readiness.

## What the Scanner Checks

- Route definitions in `App.jsx`.
- Route-to-href alignment.
- Duplicate route keys.
- Duplicate hrefs.
- Missing `title`.
- Missing `navLabel`.
- Missing navigation `group`.
- Status distribution across route definitions.
- Production operations route configuration keys for `dashboard`, `workflow`, and `role-admin`.

## Generated Reports

- `docs/demo/021_ROUTE_INTEGRITY_REPORT.md`
- `docs/demo/021_ROUTE_INTEGRITY_REPORT.json`

## Why This Matters

The demo flow depends on predictable route names, labels, navigation groups, and route-to-hash consistency. This report gives us a release-hardening checkpoint before role-based demo scripts and browser validation.
