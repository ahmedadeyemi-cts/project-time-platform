# Module 069 Security and Operations

- Session and scope enforcement occurs in the backend.
- Broad, team, and self scopes are computed from active roles and permissions.
- Search/category/lifecycle filters are parameterized SQL inputs.
- Only existing `app_users`, `resource_profiles`, and
  `resource_qualifications` records are queried.
- No endpoint adds, changes, acknowledges, renews, or deletes a record.
- Raw exception text is not returned to the browser.
- Expired qualifications are never counted as current.
- A missing expiration remains explicitly `No expiration recorded`; it is not
  silently represented as permanently valid.
- The package creates no database, Azure, Entra, or deployment artifact.
