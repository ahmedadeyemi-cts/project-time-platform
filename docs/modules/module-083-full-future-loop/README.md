# Module 083 — Full Future Loop

Module 083 turns the Full Future Loop architecture into a persistent, interactive, end-to-end sandbox inside Pulse.

## Integration status

The source package is integrated into the Pulse runtime on the dedicated Module 083 feature branch. The module is registered in Module Availability, the installed-module catalog, its guarded runtime route, and the role-aware navigation under **Platform Operations**. Migration execution and environment deployment are intentionally excluded from the source pull request.

## Purpose

The module demonstrates and validates the complete governed lifecycle:

1. Selective STEER-IT governance.
2. Private development and internal evidence.
3. Isolated canary verification.
4. Curated promotion to sandbox production.
5. Read-only production evidence.
6. Watcher normalization and private issue relay.
7. Private repair, review, and fix.
8. Repair canary verification.
9. Curated re-promotion.
10. Final outcome verification and closure.
11. Agent Keep support guidance and governed issue creation.

## Safety boundary

Module 083 is intentionally a **persistent sandbox**, not a deployment engine.

It does not:

- create or change a GitHub branch, commit, pull request, issue, release, or tag;
- call GitHub, a cloud API, a deployment controller, or a production endpoint;
- read or write secrets;
- execute a production deployment or rollback;
- read private source through Agent Keep;
- permit writes during Administrator View-As.

Each test transition writes append-only evidence to the Pulse database. This makes the complete lifecycle testable without exposing production or private-development systems.

## Route and API

- Module: `083`
- Route: `#full-future-loop`
- API root: `/api/full-future-loop`
- Migration: `082_module_083_full_future_loop.sql`
- Rollback: `082_module_083_full_future_loop_rollback.sql`

## Main API surfaces

- `GET /capabilities`
- `GET /access`
- `GET /summary`
- `GET /loops`
- `GET /loops/{loopId}`
- `POST /loops`
- `POST /loops/{loopId}/actions`
- `POST /loops/{loopId}/run-full-sandbox`
- `POST /loops/{loopId}/reset`
- `POST /loops/{loopId}/agent-keep`
- `GET /loops/{loopId}/history`

## Persistence

Migration 082 creates:

- `full_future_loop_items` — mutable current-state record with optimistic revision control;
- `full_future_loop_events` — append-only stage transitions and audit evidence;
- `full_future_loop_artifacts` — append-only decision, build, canary, release, production, support, repair, and verification artifacts;
- Module 083 RBAC permissions and role grants;
- the Module 083 feature-catalog registration.

## Access model

- View access is available to authorized platform, engineering, support, project, management, and executive roles.
- Sandbox execution requires an authorized operations or management role or `RUN_FULL_FUTURE_LOOP_SANDBOX_083`.
- Reset and administrative controls require `MANAGE_FULL_FUTURE_LOOP_083` or an equivalent administrator/release-manager role.
- View-As remains read-only.

## User experience

The UI preserves the architecture of the supplied Full Future Loop diagram while making every node interactive. It includes:

- light and dark mode support;
- responsive enterprise cards and status indicators;
- a loop inventory and current-stage summary;
- step-by-step lifecycle actions;
- a one-click complete sandbox run;
- passing and failing canary scenarios;
- Agent Keep support questions and governed support-issue evidence;
- append-only event and artifact history;
- safe reset into a new test iteration while retaining prior evidence.

See [TESTING.md](./TESTING.md) for the complete validation and UAT procedure.
