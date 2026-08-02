# Module 069 API Contract

## Read-only matrix

### `GET /api/qualifications/capabilities`

Returns the matrix source contract, server-derived access scope, available
capabilities, and explicit boundaries.

### `GET /api/qualifications/matrix`

Optional query parameters:

| Parameter | Values |
|---|---|
| `search` | Person, email, function, category, name, or competency text |
| `category` | Exact qualification category |
| `status` | `all`, `current`, `expiring`, `expired`, `unrecorded` |

The response includes role scope, effective identity, summary counts, category
options, identity-backed people rows, qualification rows, and effective dates.
These two endpoints execute read-only queries.

## Governed self-service

### `GET /api/qualifications/self-service`

Returns only the authenticated user’s qualification and certification records
and these authorization signals:

```json
{
  "access": {
    "effectiveUserId": "uuid",
    "canEditOwn": true,
    "isViewAs": false,
    "scope": "self"
  }
}
```

### `POST /api/qualifications/self-service`

Creates one record for the actual authenticated user.

### `PUT /api/qualifications/self-service/{qualificationId}`

Updates a record only when both the qualification ID and `user_id` belong to
the actual authenticated user.

Request body:

```json
{
  "category": "Certification",
  "name": "Cisco CCNP Collaboration",
  "competency": "Professional",
  "yearsOfExperience": 4.5,
  "effectiveStartDate": "2026-08-01",
  "effectiveEndDate": "2029-08-01"
}
```

The client cannot submit a user ID. Server-side identity binding prevents
cross-user creation or update.

## Authorization rules

- Project Management, Engineering, approved leads, Solution Architecture,
  Managers, PTC, and administrators may use own-profile self-service when their
  active role or explicit permission authorizes it.
- `MANAGE_OWN_QUALIFICATIONS_069` grants own-profile editing only.
- `VIEW_QUALIFICATIONS_069` grants the server-authorized read scope.
- Administrator View-As may change the effective read scope but never permits
  POST or PUT.
- Actual and effective user IDs must match for mutation.
- There is no delete endpoint.

## Validation and response safety

- Category and name are required and limited to 255 characters.
- Competency is limited to 100 characters.
- Experience must be between 0 and 99.99 years.
- An end/expiration date cannot precede the effective start date.
- Raw exception text, credentials, tokens, and secret values are not returned.
- Successful mutation responses contain identifiers and sanitized status only.
