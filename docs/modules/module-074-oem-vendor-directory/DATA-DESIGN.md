# Module 074 data design

The current phase deliberately uses a validated, unsaved draft. This document defines the future canonical shape without creating a schema.

## Vendor record

- stable vendor identifier
- vendor name (required and unique)
- OEM category (required)
- controlled lifecycle/delivery status
- optional HTTPS website
- delivery notes
- effective audit metadata when persistence is authorized

## Child records

- contacts: name, role, email, phone
- support links: label and HTTPS URL
- certifications: certification name plus future optional expiration metadata
- products: product name plus future optional delivery classification

## Future persistence controls

Any database phase must add normalized tables or an approved equivalent, foreign keys, uniqueness constraints, concurrency control, created/updated identity, immutable audit history, archival behavior, and rollback. It must not infer or seed real vendors from source code.

No database migration is included in Module 074 source-only work.
