# 056C Dashboard Route Isolation Full-File Audit

Generated: 2026-07-15T03:08:26.728633+00:00

## Files read in full

| File | Lines | Bytes | SHA-256 |
|---|---:|---:|---|
| `src/frontend/project-time-web/index.html` | 11962 | 461745 | `c0dc1d4df74ec6ac7ddd6befc52529efc4165f703a210827461198dcd8d1fd8e` |
| `src/frontend/project-time-web/src/App.jsx` | 7912 | 307617 | `eeaf278455e7f2ef3e90ed8cf22fe6178769a48ca5c28966d387a106c6bff723` |
| `src/frontend/project-time-web/src/main.jsx` | 14 | 418 | `c4f1f1ce3f63ccb7c6250453a101fc3b0b1fc27ea864d7b35237e1799cc8bd85` |
| `deployment/containers/web/Dockerfile` | 48 | 1264 | `ffbcd54d5d7ceefc47a278f44761284890087ec802fdd1a478bcf1333703f74f` |
| `deployment/containers/web/default.conf.template` | 50 | 1464 | `9bb6f7858c585460dcd5c40e81c6afa0b03314f669bcee0992609c27e61e7242` |
| `deployment/containers/web/projecttime-web-entrypoint.sh` | 32 | 556 | `b5490024dfe93a172b0926248298429a6a447f60c823b92f40a5dfa997744482` |

## Full index inventory

- Total `projectpulse-*` IDs found: 56
- Literal card-related IDs found: 20
- Module labels found: [23, 24, 25, 26, 27, 28, 29, 30]
- MutationObserver occurrences: 5
- `insertAdjacentElement` occurrences: 2
- `appendChild` occurrences: 25
- `.app-shell` occurrences: 1

## Root cause

The 056B classifier required each injected card to be inside `.app-shell`. Legacy dashboard cards are inserted outside the React application shell, so they were rejected before their strong IDs and module labels were evaluated.

## Corrective behavior

- Scans the complete document rather than assuming React ownership.
- Evaluates explicit dashboard IDs before route-shell exclusions.
- Preserves standalone route shells, pages, drawers, modals, and panels.
- Reapplies visibility after child and relevant attribute mutations.
- Exposes runtime marked-card and visible-offender diagnostics.

## Card-related literal IDs identified

- `projectpulse-022d-card`
- `projectpulse-022d-topbar-card`
- `projectpulse-022e-card-actions`
- `projectpulse-022e-dashboard-notification-card`
- `projectpulse-022e-dashboard-notification-card-script`
- `projectpulse-022e-dashboard-notification-card-style`
- `projectpulse-024-cardlet`
- `projectpulse-024-intake-card`
- `projectpulse-025-sow-card`
- `projectpulse-026-cardlet`
- `projectpulse-026-crm-card`
- `projectpulse-027-cardlet`
- `projectpulse-027-handoff-card`
- `projectpulse-028-ai-time-card`
- `projectpulse-028-cardlet`
- `projectpulse-029-cardlet`
- `projectpulse-029-uat-card`
- `projectpulse-030-reporting-card`
- `projectpulse-056b-dashboard-card-route-guard`
- `projectpulse-056b-dashboard-card-route-style`

## Validation requirements

- The obsolete `.app-shell` rejection must not exist in the guard.
- The guard version must be `056C`.
- The full-document ID scan must remain enabled.
- Mutation observation must include relevant visibility attributes.
- Runtime diagnostics must expose visible offender count.
