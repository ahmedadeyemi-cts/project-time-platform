# Group 7 — AI Provider Reliability, Help, and System User Guide

## Purpose

Group 7 stabilizes the non-secret AI-provider readiness experience in Module 064, gives Help a governed source hierarchy and saved answer-detail preferences, and renames Module 999 to **System User Guide**.

This package is stacked on Group 6 so Module 064 and Module 999 consume the official US Signal image asset and reusable enterprise presentation components. It does not create another branding system.

## Module 064 — provider reliability

The authenticated application shell starts one shared readiness controller. It:

- loads provider readiness during authenticated startup;
- restores the last verified non-secret status from browser persistence;
- refreshes in the background and when the browser regains focus;
- prevents duplicate concurrent readiness requests;
- preserves the last verified state when a later refresh fails;
- records the last check and last verified timestamps;
- exposes a manual **Retest providers** action; and
- never stores or returns API keys, credentials, provider secrets, prompts, or response content.

The normalized readiness states are:

1. `checking`
2. `available`
3. `unavailable`
4. `not_configured`
5. `authentication_failed`
6. `rate_limited`
7. `provider_error`

A cached status is clearly identified as stale when a fresh probe cannot be completed. Saving a new key or model still requires Module 064's existing fresh verification flow; a prior cached status is not proof that newly saved configuration works.

## Saved answer-detail preferences

Help includes the following saved defaults:

- Concise
- Standard
- Detailed
- Highly detailed
- Technical
- Executive
- Step-by-step
- Include repository context
- Include assumptions
- Include source citations

The preference is saved for the signed-in browser identity without adding a database schema. The current question can override the saved default by using `/concise`, `/detailed`, `/highly-detailed`, `/technical`, `/executive`, or `/step-by-step`, or by explicitly requesting or excluding repository context, assumptions, or citations.

The effective preference is attached to the governed Help request and shapes which supporting sections are displayed. The response records whether the effective value came from a saved preference or a query override.

## Governed Help hierarchy

Help uses this source order:

1. **System User Guide**
2. **Module descriptions and API metadata**
3. **Repository documentation**
4. **Permission-aware AI repository search**
5. **Escalation or issue creation when no verified answer exists**

Help must use the highest verified source available, identify source limitations, and never convert missing evidence into a confident unsupported answer. When verified guidance cannot be produced, the user can open **Report an Issue** or **Feature Request** in Module 076.

## Module 999 — System User Guide

The former title **ProjectPulse Complete User Guide** is replaced with **System User Guide** in the page and module registry.

The existing searchable guide remains generated from current module metadata and detailed guide definitions. Group 7 adds the official US Signal logo and an authoritative-source overview that states the intended coverage:

- every active module;
- role access;
- common tasks;
- screenshots or maintained evidence references;
- expected outcomes;
- troubleshooting and errors;
- support references;
- cross-module workflows;
- glossary;
- integration setup;
- reporting instructions; and
- links to Report an Issue and Feature Request.

Documenting a module does not grant access to that module. Existing server and frontend permissions remain authoritative.

## PR #277 compatibility

PR #277 owns Pulse AI Help chat usability: conversation scrolling, keyboard behavior, direct product-purpose answers, and related runtime validation. Group 7 does not recreate or replace that implementation.

The Group 7 branch is reconciled with the main-line PR #277 merge and validates that its Help source integration remains compatible with the current generated frontend and complete production build.

## Group 6 dependency

Group 7 is intentionally stacked on Group 6. It imports Group 6 enterprise components for Module 064 readiness and Module 999 official branding. After Group 6 merges, Group 7 must be retargeted to the resulting current `main` and fully revalidated.

## Shared-file overlap

The only existing shared file changed directly by Group 7 is frontend `package.json`, where the idempotent installer and validator are added after Group 6.

The installer updates generated or module-specific frontend files during predevelopment and prebuild:

- generated `App.jsx` receives one authenticated readiness controller;
- Module 064 receives one stable readiness panel;
- Help receives the governed hierarchy and preference-aware answer rendering;
- Module 999 receives the official logo, new title, and governance overview; and
- the generated module registry receives the **System User Guide** identity.

The permission-aware More menu is not rewritten.

## Security and data boundary

- No provider secret is stored in the readiness cache.
- No provider credential is returned to Help or Module 999.
- No permission, role, record scope, or effective-user boundary is changed.
- Permission-aware repository search remains limited to evidence the effective user may access.
- Readiness does not activate a provider, change a model, or authorize external execution.

## Migration

No migration is required or included.

## Deployment

No deployment workflow, Azure resource, Container Apps resource, Test resource, Production resource, provider configuration, provider secret, model, index, or external service is changed. No deployment is performed.

## Validation

The focused Group 7 validation confirms:

- all seven provider readiness states;
- authenticated startup and background refresh;
- last-verified continuity and timestamps;
- duplicate-request prevention;
- manual Retest;
- non-secret persistence;
- all saved preference choices and query overrides;
- the five-tier Help hierarchy;
- Module 999 rename and official logo;
- Report an Issue and Feature Request links;
- Group 6 dependency and PR #277 compatibility;
- idempotent generated integration;
- complete frontend production build; and
- full ProjectPulse CI.
