# Module 069 API Contract

## `GET /api/qualifications/capabilities`

Returns the source contract, server-derived access scope, available read-only
capabilities, and explicit mutation/notification locks.

## `GET /api/qualifications/matrix`

Optional query parameters:

| Parameter | Values |
|---|---|
| `search` | Person, email, function, category, name, or competency text |
| `category` | Exact qualification category |
| `status` | `all`, `current`, `expiring`, `expired`, `unrecorded` |

The response includes:

- role scope and effective user;
- summary counts and category list;
- identity-backed people coverage rows;
- qualification rows and effective dates; and
- explicit limitations.

## Scope rules

- Administrator, Project Team Coordinator, and Executive: organization scope.
- Manager/team lead and users with scheduling/team permissions: matching team or
  department plus self.
- Other active users: self only.
- View-As may change the effective read-only scope but never creates write power.

Both endpoints require a valid session, execute `SELECT` only, return sanitized
errors, and expose no mutation route.
