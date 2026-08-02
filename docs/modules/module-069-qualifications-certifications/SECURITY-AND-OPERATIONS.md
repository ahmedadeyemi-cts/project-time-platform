# Module 069 Security and Operations

## Read model

- Session and scope enforcement occurs in the backend.
- Broad, team, and self scopes are computed from active roles and permissions.
- Search, category, and lifecycle filters are parameterized SQL inputs.
- The matrix reads existing `app_users`, `resource_profiles`, and
  `resource_qualifications` records.
- Expired qualifications are never counted as current.
- A missing expiration remains explicitly `No expiration recorded`; it is not
  represented as permanently valid.

## Self-service mutation boundary

- POST and PUT operate only on the actual authenticated user’s own
  `resource_qualifications` rows.
- The request body contains no user ID; the server supplies the actual session
  user ID.
- PUT includes both `resource_qualification_id` and `user_id` in its predicate.
- View-As is always read-only.
- A mismatch between actual and effective user IDs blocks mutation.
- There is no delete route.
- Organization-wide editing is not implied by self-service permission.
- Input size, date order, text lengths, and experience ranges are validated.
- Shared module-audit evidence is written when that optional table is present;
  absence of the optional audit table does not corrupt qualification storage.

## Administrator behavior

An actual Super Administrator may use Module 069 and its own-profile editing in
that administrator’s own session. Permanent administrator authority does not
transfer to an effective View-As user. Editing another person’s qualification
requires a separately reviewed administrative workflow and is not exposed by
these endpoints.

## Operational safety

- Raw exception text is suppressed from browser responses.
- No credential, token, private key, provider secret, or connection string is
  accepted or returned.
- The change uses the existing qualification table; migration 063 changes RBAC
  permissions and does not create a second qualification data store.
- No Azure, Entra, external provider, mail-delivery, or deployment operation is
  performed by Module 069 self-service.
