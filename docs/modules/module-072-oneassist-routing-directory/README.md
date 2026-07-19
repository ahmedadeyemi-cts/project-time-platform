# Module 072 — OneAssist Routing PIN Directory

Module 072 brings the existing US Signal OneAssist customer routing directory into ProjectPulse. The confirmed PIN classification is a public routing identifier: PINs are displayed without masking to every ProjectPulse user and through a versioned public read API. Knowledge of a PIN must never be treated as authentication or proof of identity.

## Confirmed authorization

- Everyone can view customer names, IDs, and routing PINs.
- Canonical `MANAGER`, `ADMINISTRATOR`, `SUPER_ADMINISTRATOR`, and `PROJECT_TEAM_COORDINATOR` roles can add, edit, import, remove, and save routes.
- Solution Architects and all other roles remain viewers unless separately authorized later.
- Mutation authorization comes from the actual ProjectPulse session. View-As never transfers authority.
- The governed permission label is `MANAGE_ONEASSIST_ROUTING_DIRECTORY`.

## Preserved behavior

- Customer name, stable customer ID, and exactly five-digit routing PIN.
- Unique PIN enforcement.
- Search by name, PIN, or customer ID.
- Add, edit, remove, refresh, and explicit save.
- CSV and XLSX import with a non-persistent preview.
- Standard CSV and IVR CSV download.
- Public full-directory and PIN-resolution GET APIs.

## Source-package boundary

The release train uses the existing Cloudflare service as a compatibility store. It does not modify Cloudflare, create a ProjectPulse table, migrate PIN values, or configure a service credential. Its public read routes are registered in source; without approved Cloudflare credentials the adapter remains unavailable and makes no external change.

`Program.cs`, `App.jsx`, `package.json`, deployment files, and the frontend validator chain are semantically integrated once from the Module 002-enabled current-main base.

## Environment names

- `PROJECTPULSE_ONEASSIST_UPSTREAM_BASE_URL`
- `PROJECTPULSE_ONEASSIST_ACCESS_CLIENT_ID`
- `PROJECTPULSE_ONEASSIST_ACCESS_CLIENT_SECRET`

The adapter can fall back to the corresponding `PROJECTPULSE_ONCALL_*` values when both modules share one Cloudflare application. Values are never committed or returned.

## Branding

The React center uses the existing repository-owned US Signal logo and ProjectPulse US Signal blue, cyan, and green brand tokens. The unmasked PIN treatment, tables, import preview, public API panel, and footer are all scoped to Module 072.

## External state

- Azure changes: none.
- Database changes: none.
- Entra changes: none.
- Cloudflare changes: none.
- Commit, push, and deployment: not performed.
