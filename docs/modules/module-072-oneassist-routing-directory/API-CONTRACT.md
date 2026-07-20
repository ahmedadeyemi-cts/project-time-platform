# Module 072 API Contract

## Authenticated endpoints

| Method | Route | Authority | Result |
|---|---|---|---|
| `GET` | `/api/oneassist/capabilities` | Any ProjectPulse user | Classification, authorization, import, public API, and persistence metadata |
| `GET` | `/api/oneassist/routes` | Any ProjectPulse user | Complete unmasked routing directory |
| `PUT` | `/api/oneassist/routes` | Manager, Administrator, Super Administrator, or PTC | Validates and saves the complete directory |
| `POST` | `/api/oneassist/import/preview` | Manager, Administrator, Super Administrator, or PTC | Parses CSV/XLSX and returns a non-persistent preview |

## Public endpoints

| Method | Route | Result |
|---|---|---|
| `GET` | `/api/public/v1/oneassist/routes` | Complete public routing directory |
| `GET` | `/api/public/v1/oneassist/resolve?pin=12345` | Matching routing record or `404 route_not_found` |

Public endpoints are GET-only, cross-origin readable, and briefly cacheable. They expose no mutation operation or retired external compatibility service credential.

## Route shape

```json
{
  "id": "stable-customer-id",
  "name": "Customer name",
  "pin": "12345"
}
```

PIN values are strings so leading zeroes are preserved. They must contain exactly five ASCII digits and be unique across the directory.

## Import headers

CSV/XLSX import accepts these case-insensitive headers:

- `name` or `customer_name`
- `pin`
- `id` or `customer_id`

Files are limited to 5 MiB. A preview reports valid rows and warnings and never persists automatically.

## Error boundary

Raw upstream responses, secret values, connection strings, and exception text are not returned. Public resolution deliberately returns the public routing PIN and matching customer route.

## PROJECTPULSE_NATIVE_POSTGRESQL_MIGRATION_031

- Source parent: `603538ad408b70b3e6a26ff2f4f162599fa1cabf`
- Migration source: `database/migrations/031_modules_071_072_native_persistence.sql`
- Rollback source: `database/rollback/031_modules_071_072_native_persistence_rollback.sql`
- Module 071 persistence: ProjectPulse PostgreSQL schedule, roster, acknowledgement, and history tables
- Module 072 persistence: ProjectPulse PostgreSQL routing directory and immutable revision tables
- Platform Administrator authority: explicit
- View-As write authority: blocked
- External compatibility runtime dependency: removed
- Migration applied: no
- Database changed: no
- Deployment performed: no
