# Module 074 API contract

Base path: `/api/oem-vendor-directory`

All endpoints require an authenticated ProjectPulse session. No endpoint writes to a database or external service.

| Method | Path | Authority | Purpose |
| --- | --- | --- | --- |
| `GET` | `/capabilities` | Every authenticated user | Returns module metadata, actual-session authority, fields, and the persistence lock. |
| `GET` | `/directory` | Every authenticated user | Returns the canonical directory source. In this phase it returns an empty set and an explicit `directory_source_not_configured` state. |
| `GET` | `/reference` | Every authenticated user | Returns controlled statuses, suggested categories, and HTTPS link policy. |
| `POST` | `/validate` | Approved editors | Normalizes and validates at most 500 draft vendors; returns normalized data and row-level errors without persistence. |

## Validation rules

- Vendor name and OEM category are required.
- Vendor names are case-insensitively unique within a draft.
- Status must be one of `active`, `preferred`, `limited`, `inactive`, or `under_review`.
- Website and support URLs, when supplied, must use HTTPS.
- Contact name is required; contact email is validated when present.
- The server accepts structured arrays and returns `persistencePerformed: false`.
- Malformed JSON and non-string scalar fields fail safely without returning internal exception details.

`Program.cs` registration is deferred to the shared-file integration checkpoint.
