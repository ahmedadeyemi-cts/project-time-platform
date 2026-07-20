# Module 066 — Project FlowHive API Contract

## Contract state

All routes are module-owned and registered exactly once in this uncommitted
source package. They are not merged, deployed, or runtime-verified. Every route
requires a ProjectPulse session. Canonical portfolio reads use the effective
View-As identity; future persistence/baseline actions must use the actual actor
identity and server-side project authorization.

## Read routes

### `GET /api/project-flowhive/capabilities`

Returns phase flags, evidence-based capabilities, integration readiness, Module
062/064 dependencies, and the governed US Signal logo checksum.

### `GET /api/project-flowhive/portfolio`

Returns backend-scoped canonical `projects`, `tasks`, and `assignments` without
mutation. Assignment records include `resourceUserId`, `resourceName`, and
`resourceEmail` so the frontend identity dropdown preserves ProjectPulse IDs.

### `GET /api/project-flowhive/readiness`

Returns 066A.1–066E source status, verified Module 002 source/merge commits,
shared-file status, and activation blockers.

### `GET /api/project-flowhive/artifacts/readiness`

Returns internal PDF/XLSX availability, the repository US Signal logo SHA-256,
and the customer-sharing lock.

## Side-effect-free computational routes

### `POST /api/project-flowhive/planning/validate`

Accepts `ProjectFlowHivePlanRequest` and validates:

- numeric dotted WBS hierarchy and unique WBS values;
- parent/child references;
- duration, milestone, progress, effort, and constraint values;
- FS, SS, FF, and SF dependencies;
- positive/negative lead/lag bounds;
- duplicate dependencies and dependency cycles;
- assignment WBS references and Module 062-backed identity GUIDs;
- allocation and planned-hour bounds;
- maximum request sizes.

No record is stored.

### `POST /api/project-flowhive/schedule/calculate`

Returns a deterministic weekday preview containing earliest/latest indices,
start/finish dates, total/free float, critical-task flags, project finish, and
planned-hour summary. `calendarMode` explicitly states that Module 057 holiday
authority is not applied.

### `POST /api/project-flowhive/ai/request-preview`

Returns a sanitized `project_flowhive_plan` request compatible with Module 064,
the required `claude → openai → local_template` route, refusal behavior, source
authority, and deterministic local fallback. It does not resolve or call a
provider.

### `POST /api/project-flowhive/artifacts/pdf-preview`

### `POST /api/project-flowhive/artifacts/excel-preview`

Both endpoints require:

- `audience = "internal"`;
- `acknowledgeInternalDraft = true`;
- a valid calculable plan.

They return transient bytes only. The actual repository US Signal logo is
embedded and verified. The result is marked as an internal draft. No artifact
record, customer link, or delivery is created.

## Explicit locked routes

### `POST /api/project-flowhive/plans/drafts`

Always returns HTTP 423 `persistence_locked` in this package.

### `POST /api/project-flowhive/plans/{planId}/baseline`

Always returns HTTP 423 `baseline_locked` in this package.

These routes document the future boundary without creating hidden in-memory or
filesystem persistence.

## AI contract

The only allowed execution integration is:

```text
ProjectPulseAiRouter.GenerateAsync(
  feature: ProjectPulseAiFeatures.ProjectFlowHivePlan,
  route: Claude -> OpenAI -> local_template,
  refusal: stop without failover)
```

Module 066 contains no `HttpClient`, provider SDK, provider URL, API-key read,
or secret field.

## Persistence contract

`IProjectFlowHivePlanRepository` is the module boundary. Only
`LockedProjectFlowHivePlanRepository` exists in this package and reports
`WritesEnabled = false`. No database artifact is present or applied.

## Error/lock responses

| HTTP | Status | Meaning |
|---:|---|---|
| 400 | `validation_failed` or acknowledgement error | Request cannot be calculated/exported |
| 401 | `session_required` | Missing ProjectPulse session |
| 403 | `access_denied` | Canonical portfolio user/scope is invalid |
| 423 | `persistence_locked`, `baseline_locked`, `customer_export_locked` | Explicit governed capability gate |
| 503 | `configuration_missing` | Canonical database read configuration unavailable |
| 500 | Generic problem title | Server error; no provider/customer secret returned |

## Size limits

- Tasks: 500
- Dependencies: 4,000
- Assignments: 5,000
- Task duration: 1–730 working days (milestone duration 0)
- Lead/lag: -365–365 working days
- Allocation: greater than 0 and no more than 100 percent
