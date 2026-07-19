# Module 070 API Contract

All endpoints require an effective ProjectPulse session and use the established
Module 062-compatible user ID. They are read-only and return sanitized errors.

## `GET /api/capacity-forecast/model`

Returns the calculation formulas, practices, horizon limits, probability weight
policy, source ownership, access scope, and mutable scenario fields.

## `GET /api/capacity-forecast/engineers`

Returns active engineer choices inside the caller's server-authorized scope.
Each option includes a Stable `app_users.user_id` exposed as `userId`, current display name, email, job title,
team, department, primary function, and derived practice. Consumers must store
or submit the ID, never a copied display name.

## `GET /api/capacity-forecast/forecast`

Query parameters:

| Parameter | Rule |
|---|---|
| `startDate` | Optional `YYYY-MM-DD`; normalized to that week's Monday |
| `weeks` | Optional integer; clamped to 4–52, default 14 |
| `practice` | `all`, `collaboration`, `systems`, `networking`, or `other` |
| `engineerUserId` | Optional stable identity ID; rejected outside caller scope |
| `supplementalHoursPerWeek` | Optional numeric scenario value from 0–10,000; not persisted |

The response includes filters, access evidence, summary totals, continuous weekly
rows, request-level demand evidence, calculation rules, and source limitations.
Net demand is clamped to zero after subtracting supplemental scenario capacity,
so remaining capacity and utilization never rely on negative demand.

## Mutation boundary

There is no `POST`, `PUT`, `PATCH`, or `DELETE` endpoint. User/name maintenance
belongs to User Administration. Request and staffing-date maintenance belongs to
Module 020. Persistent supplemental/LTE capacity requires an approved canonical
data source and separate database-change authorization.
