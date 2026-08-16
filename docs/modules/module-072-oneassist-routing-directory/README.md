# Module 072 — OneAssist Routing PIN Directory

Module 072 provides the US Signal OneAssist customer routing directory inside authenticated Pulse.

## Confirmed authorization

- OneAssist customer names, IDs, and five-digit routing PINs require a valid Pulse session.
- All authenticated Pulse users can view and search the unmasked routing directory.
- `MANAGER`, `PROJECT_TEAM_COORDINATOR`, canonical `ENGINEERING_LEAD`, legacy `ENGINEERING_TEAM_LEAD`, `SUPER_ADMINISTRATOR`, and legacy `ADMINISTRATOR` can add, edit, import, remove, and save routes.
- All other authenticated roles remain read-only.
- View-As never transfers mutation authority.
- The public On-Call page and public On-Call APIs never request or return OneAssist PINs.
- The governed permission label is `MANAGE_ONEASSIST_ROUTING_DIRECTORY`.

## Preserved behavior

- Customer name, stable customer ID, and exactly five-digit routing PIN.
- Unique PIN enforcement.
- Search by name, PIN, or customer ID.
- Add, edit, remove, refresh, and explicit save for approved editors.
- CSV and XLSX import with a non-persistent preview.
- Standard CSV and IVR CSV download after authentication.
- Pulse PostgreSQL persistence and immutable revision evidence from migration 031.

## Security and release boundary

PINs are routing identifiers rather than authentication credentials, but they are internal operational data and are not exposed anonymously. No new migration, secret, infrastructure, environment, or deployment change is introduced by this follow-up.
