# Module 065: Teams sender and personal app repair

This completes the application work omitted from UI-only PR1155. It preserves that
workspace styling. Do not describe package generation or CI as Microsoft tenant
approval, personal installation, notification receipt, or user acknowledgement.

## Implemented

- The sender no longer posts a Pulse dashboard URL as a text activity topic.
  It resolves the exact recipient, reads the personal installation filtered by the
  saved manifest ID, validates its catalog/Entra association, and uses the supported
  Graph `teamsAppInstallation` entity topic. The catalog ID is used to disambiguate
  the sending app. IDs are not interchangeable and no tab ID is invented.
- Every send uses the actual environment-specific stored Module065 services
  metadata and its existing encrypted credential envelope. No SSO or mutable shared
  M365 credential fallback. Only exact environment-bound deployment credentials
  matching both stored identifiers are eligible when no stored secret row exists.
- The existing durable claim remains before external calls. There is one send,
  no implicit installation, no policy mutation, no retry after an unknown outcome,
  and no email replay. Configuration revision, credential version, recipient boundary
  and manual caller authority are rechecked immediately before sending.
- Same-origin, actual-admin `check-installation` verifies one recipient without
  sending. Other administrators remain self-only; SuperAdmins may check another
  tenant recipient. It is not a bulk directory export or an installation permission.
- Provider response bodies are bounded. A known Graph error code and GUID request
  identifier are retained with fixed actionable guidance. Raw provider prose,
  tokens and secret values are not stored or returned. Existing diagnostic rows
  remain readable. The existing text column holds the new structured evidence;
  no schema migration or live configuration rewrite is required.
- New personal app package 1.0.2 keeps the supplied Teams and services-client IDs.
  It defines a real Notifications tab, valid domain and scoped notification and
  installation-read permissions. No bot or chat permission is added.
- Only `/teams-notifications/index.html`, a data-free public landing page, can be
  framed by Microsoft Teams. The existing application/API framing protection stays
  unchanged. The landing initializes the pinned Teams SDK and opens Pulse separately
  with ordinary browser authentication. No business records or approval actions are
  exposed to the iframe, and query parameters cannot control its destination.

## Microsoft administrator rollout

After the corrected web build is installed in Protected UAT:

1. Generate `PulseApp-UAT-1.0.2.zip` with `scripts/teams/build-package.py`, or use the
   identical validated CI artifact. It contains only manifest.json and both PNGs.
2. In the matching tenant's Teams admin center, locate the existing PulseApp under
   Teams apps / Manage apps. Update that app with this package. Do not create new
   IDs or replace the working Pulse SSO registration.
3. Make PulseApp available to the pilot member, then install/update it in that
   user's personal Teams scope. Review/consent to `TeamsAppInstallation.Read.User`
   alongside the existing notification permission. This scoped read supports the
   new installation check; it cannot install apps, read chats, or bypass app policy.
4. In Module065 Test, keep the saved manifest ID. Use the verified sign-in address,
   not an unrelated email alias, then Check installation (no notification).
5. Only after installation is verified, explicitly SEND TEAMS TEST once. Require
   HTTP204 acceptance, the recipient's Activity entry, and a working Open Pulse
   destination. Preserve error/request evidence on failure instead of repeated sends.

The unavailable-app dialog has multiple possible tenant causes. No connected
Teams/Entra admin session is provided to this change; publishing, user availability,
installation and consent remain administrator actions. The package fixes the
missing personal capability but does not prove that was the dialog's sole cause.
Missing scoped-read consent can yield installation HTTP403 rather than a conclusive
not-installed result. Do not infer that an app is blocked solely from that response.

## Scope boundaries

Pilot support is for active member users in the configured tenant. Guest accounts
and ambiguous installations fail closed. Automatic dispatch still follows existing
recipients and `production_governed` policy. Test-only never turns into automatic
live delivery. Independent email/Teams queuing and richer event-specific previews
are separate changes, not claimed by this repair. No Module064 inference is used.

## Validation and rollback

Run the focused .NET executable, the complete API and frontend builds, existing
Teams state tests and unchanged platform contrast checks. CI validates the manifest
against Microsoft's schema and package icon bytes; tests actual Nginx response
headers and the built landing in a synthetic Teams host at mobile/desktop and both
themes. The real pinned SDK download is checked independently. No live Microsoft
calls, policy changes or production delivery occur in these tests.

Use the existing protected exact-main UAT controller after required checks pass.
No controller changes or bypasses are introduced. On an application failure use
its existing immutable-image rollback. Keep the package manifest ID unchanged;
rolling back the installed Teams package needs a separately reviewed newer version.
Do not erase delivery evidence or reset unknown-outcome claims to force a retry.

Official API contracts:
https://learn.microsoft.com/en-us/graph/api/userteamwork-sendactivitynotification
https://learn.microsoft.com/en-us/graph/api/userteamwork-list-installedapps
https://learn.microsoft.com/en-us/microsoftteams/platform/tabs/how-to/tab-requirements
https://learn.microsoft.com/en-us/microsoftteams/teams-custom-app-policies-and-settings
