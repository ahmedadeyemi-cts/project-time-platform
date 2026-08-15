# Module 072 API Contract

## Authenticated endpoints

| Method | Route | Authority | Result |
|---|---|---|---|
| `GET` | `/api/oneassist/capabilities` | Any authenticated Pulse user | Classification, authorization, import, and persistence metadata |
| `GET` | `/api/oneassist/routes` | Any authenticated Pulse user | Complete unmasked routing directory |
| `PUT` | `/api/oneassist/routes` | Manager, Engineering Lead, PTC, or platform administrator | Validate and save the complete directory |
| `POST` | `/api/oneassist/import/preview` | Manager, Engineering Lead, PTC, or platform administrator | Parse CSV/XLSX and return a non-persistent preview |

## Anonymous boundary

There is no anonymous OneAssist directory or PIN-resolution endpoint. The former `/api/public/v1/oneassist/*` routes are not registered. The public On-Call page and public On-Call APIs remain separate and contain no OneAssist PIN data.

## Route shape

```json
{
  "id": "stable-customer-id",
  "name": "Customer name",
  "pin": "12345"
}
```

PIN values remain strings so leading zeroes are preserved. They must contain exactly five ASCII digits and remain unique.

## Import headers

CSV/XLSX import accepts these case-insensitive headers:

- `name` or `customer_name`
- `pin`
- `id` or `customer_id`

Files are limited to 5 MiB. A preview reports valid rows and warnings and never persists automatically.

## Error boundary

Unauthenticated requests receive the standard session-required response and no customer or PIN data. Raw exceptions, secrets, and connection strings are not returned. View-As can read the effective-user directory but cannot edit, import, or save it.
