# Module 070 — Capacity & Pipeline Forecasting

Module 070 replaces the manual `PS Capacity Planning(1).xlsx` workflow with a
role-scoped, continuous weekly forecast over existing ProjectPulse data. It
preserves the workbook's core arithmetic while adding explicit remaining
capacity, utilization, probability weighting, identity-backed engineer choices,
and input guards.

## Delivered source scope

- Live engineer dropdown keyed by stable `app_users.user_id`, with display names,
  email, title, team, department, function, and practice read from the shared
  ProjectPulse identity foundation.
- Editable start date, 4–52 week horizon, practice, engineer, and non-persistent
  supplemental/LTE scenario hours. These controls can be changed at any time.
- Continuous Monday-based weekly rows without the workbook's 2024-to-2026 gap.
- Committed capacity from `resource_capacity_plans` and open future demand from
  `engineering_resource_requests` and its assignment table.
- Weighted unfilled pipeline hours distributed across overlapping weeks.
- Available, committed, weighted-pipeline, supplemental, net-demand, remaining,
  utilization, and constraint-state calculations.
- Server-side organization, team, or self scope and selected-engineer scope
  enforcement.
- Documentation, validation, route/navigation registration, build guard, and
  central governance records.

## Ownership boundaries

Module 070 does not create another user directory. Add or rename engineers in
User Administration; refreshed dropdown labels follow the Module 062 identity
approach while stable IDs preserve references. Project/request dates remain
owned by Module 020 Project Intake & Engineering Resource Requests. Module 070's
date and identity selection controls are immediately adjustable but do not write
to the database.

No opportunity dollar value is converted to labor hours. The current schema has
no approved supplemental/LTE tag, so supplemental hours are an explicit scenario
input and are never persisted or presented as source data.

## State

- Source state: `RELEASE_TRAIN_CANDIDATE_UNCOMMITTED` after the validator and build
  gates recorded by this package pass.
- Runtime state: not active until reviewed, committed, merged, deployed, and
  portal-smoke-tested.
- Database/Azure/Entra state: no change.
- Commit/push/deployment state: not performed by this package.

Validation evidence: Module 070 contract 65/65, Module 059 global-route guard,
Module 062 identity guard, Module 056E preservation, .NET 10 Release build, and
production frontend build passed. Existing frontend chunk-size and baseline
backend warnings remain outside Module 070.

## Requirement coverage

- `RES-013`: capacity and future-demand visibility.
- `RES-014`: engineering resource and pipeline planning by date/practice/person.
- `RPT-007`: weekly capacity, demand, remaining, utilization, and exception
  reporting.

See `WORKBOOK-CALCULATION-CONTRACT.md` for the calculation audit and
`OVERLAP-AND-INTEGRATION.md` for the mandatory final commit gate.
