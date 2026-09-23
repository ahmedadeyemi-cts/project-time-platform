# Module 065 Teams notification workspace and delivery repair

## Release hold

The user requested a draft PR only. Do not merge, enable auto-merge, invoke a
release controller, deploy to Test or Production, change live Microsoft settings,
rotate credentials, modify consent, or send a live notification for this work.
The present patch is the implemented **workspace/UI portion** of the repair.
It is not a completed fix for Microsoft Graph HTTP 400. The sender work below is
an explicit open item, not code claimed to be deployed or validated.

## Implemented workspace

- Reuse the existing Microsoft Integration card, form, primary-action and
  secondary-action classes. Add only panel-scoped styles using Pulse theme tokens.
- Show saved configuration, the last recorded request, and automatic-delivery
  policy as separate facts. Never label a saved checkbox as a verified connection.
- Separate delivery configuration, a single-recipient test, and recent history.
- Make form spacing, buttons, badges, loading, empty/error states, keyboard focus,
  responsive layout and dark-theme styling consistent with the surrounding portal.
- Explain known diagnostic classes while retaining the exact technical code.
  HTTP 400 alone does not establish its cause. `sent` means Microsoft accepted
  the request, not that the recipient saw it or its destination opened correctly.
- Keep existing request URLs, auth headers, revision checks and explicit test
  confirmation. The backend remains the authorization and recipient authority.
- Disable tests while draft changes are unsaved; require resetting after a
  conflicting revision. Do not silently overwrite another administrator's settings.
- Reset workspace state on environment changes. Abort abandoned browser requests,
  fence late responses and synchronously reject duplicate clicks. Cancellation
  does not imply that an external effect was undone. No automatic resend exists.
- A failed refresh disables mutations. An authentication/authorization failure
  clears the old rendered configuration and recipient/history state.
- Recent-result filters operate only on the history already returned by the API;
  there is no invented all-time total or connection-readiness score.

No user identifier, app ID, tenant ID, secret, recipient policy, sender address,
AI-provider route, migration, backend API or existing release workflow is changed.
The new CI workflow is read-only and pull-request-only; it has no live credentials,
GitHub write permission, deployment environment or dispatch operation.

## Sender repair still required before this draft is ready

1. Replace the text topic's direct Pulse `/#dashboard` destination with an approved,
   tested Teams destination for the installed app. Verify any manifest/catalog
   identifier difference. Do not invent a tab or substitute a Teams URL for the
   application's public base URL. Decide whether to store a verified destination
   in environment-scoped Module 065 configuration or use the installed-app topic.
   Any new schema requires reviewed migration and rollout integration.
2. Verify the real token-sending tenant/client ID against the selected Module 065
   services connection, not merely the separate SSO app's displayed name. Never
   read or disclose secret values to prove this match.
3. Retain bounded, safely filtered Graph error code, message and correlation ID.
   Do not return raw response bodies, tokens, user identifiers in errors, or
   credential material. Prefer a code-to-guidance mapping when a message cannot
   safely be retained. HTTP status alone is insufficient root-cause evidence.
4. Resolve each authorized recipient to its current Entra object ID or verified
   UPN. Do not guess identity from a mail alias or broaden the recipient audience.
5. Test actual configured-app installation and notification acceptance with an
   explicitly authorized pilot only after release approval. Source/CI success is
   not live receipt evidence. The Teams unavailable-app dialog is a separate
   catalog/availability/installation issue.

Independent email and Teams delivery, event-specific preview content and a
cross-channel recovery worker need their own reviewed design and tests. They are
not silently introduced by this visual patch. Preserve uncertain-outcome claims
and never resend email because Teams failed.

## Validation

Run from the repository root:

```sh
node --test tests/teams-workspace/state.test.mjs
cd src/frontend/project-time-web
npm ci --ignore-scripts
npm run build
```

Local evidence: 23 Node tests passed. JSX syntax transpilation and CSS parsing
passed using preinstalled development tools. This is not a React DOM interaction
or full application build. Local npm install could not reach the registry; the
committed CI runs the real dependency-locked frontend build without weakening
existing validators.

Before declaring UI acceptance, use synthetic data to check light/dark themes,
320/768/1440-pixel widths, keyboard navigation and zoom, long recipient addresses,
read-only/View-As, wrong environment, unsaved values, slow/failed refresh, a
concurrent revision, switching environment during a request, and rapid clicks.
Capture genuine component screenshots; do not substitute a mock illustration for
rendered UI evidence. Record any unrun cases as unverified.

## Reference constraints for sender follow-up

Microsoft requires a Teams-domain webUrl for a custom text activity topic and
requires the notification app to be installed for the recipient:
https://learn.microsoft.com/en-us/graph/teams-send-activityfeednotifications

User notification request and installed-app topic examples:
https://learn.microsoft.com/en-us/graph/api/userteamwork-sendactivitynotification

App deep links and catalog identifier considerations:
https://learn.microsoft.com/en-us/microsoftteams/platform/concepts/build-and-test/deep-link-application
