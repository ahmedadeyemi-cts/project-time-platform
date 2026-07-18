# Module 999 — ProjectPulse Complete User Guide

## Purpose

Module 999 is the in-application, searchable user guide for ProjectPulse.

It is available to every authenticated user and documents:

- global ProjectPulse navigation, session, profile, search, help, role, audit, and troubleshooting functions;
- every installed module route in the application registry;
- detailed function lists for established workflows;
- step-by-step usage;
- status meanings;
- role and access expectations;
- important operational and data-handling notes;
- a glossary and support-request checklist.

## Route

`#user-guide`

## Access

Module 999 uses an empty permission requirement so every authenticated role can see
its Dashboard card, navigation entry, and page.

The guide does not grant access to documented modules. Each module and API continues
to enforce its own roles, permissions, record-state rules, and write authorization.

## Living documentation model

`SystemUserGuide.jsx` receives the installed-module registry from `App.jsx`.

- Every registry module is automatically included in the guide.
- Detailed route-specific documentation overrides the generic entry.
- A generic explanation is generated for a registry module that has not yet received
  a detailed guide entry.
- Global platform functions are maintained alongside module documentation.

This design prevents a newly registered module from being completely absent from
Module 999 while detailed documentation is being completed.

## Files

- `src/frontend/project-time-web/src/SystemUserGuide.jsx`
- `src/frontend/project-time-web/src/system-user-guide.css`
- `src/frontend/project-time-web/src/HelpAssistant.jsx`
- `src/frontend/project-time-web/src/help-assistant.css`
- `src/frontend/project-time-web/src/App.jsx`

## Validation-marker correction

The first guarded web deployment successfully built, started, and exposed every
Module 999 and preserved-module marker. Post-deployment validation then detected
a retired user-interface label because that literal phrase appeared only in a
user-guide note explaining that the old control was unavailable. The deployment
correctly rolled back.

The guide now describes the current direct-entry and personal-default behavior
without embedding the retired UI label, keeping bundle validation unambiguous.

## Deployment status

**Status:** Complete — source committed, web deployed, technical validation passed.

**Confirmed:** 2026-07-18 UTC

### GitHub checkpoints

- Branch: `feature/module-999-complete-user-guide-20260718`
- Initial implementation commit: `31ef900f3c6e283240d333c55ea5dd54774c88d1`
- Validation-marker repair commit: `2cb4f1eb129f4b48d272a8a872b2b9f20c0e0547`

### Azure runtime

- API image: `acrphdtest7825cc.azurecr.io/project-health-dashboard-api@sha256:10185bc58252c768577a343b734a80221ed5949d1b7ad141643bc90556dc43f4`
- API revision: `ca-phd-test-api-westus3--m063api4-0717232631`
- Web image: `acrphdtest7825cc.azurecr.io/project-health-dashboard-web@sha256:8d58e465e3229b63979500cd95ffccbb44a2ef09a1397b86cd5ef0552c24fcbb`
- Web revision: `ca-phd-test-web-westus3--m999f1-0718012956`

### Validation evidence

- Public root: HTTP `200`.
- Public health: HTTP `200`.
- Unauthenticated Module 063 access endpoint: HTTP `401`.
- Module 999 bundle markers: passed.
- Preserved-module markers: passed.
- Retired UI label in deployed bundle: absent.
- Dashboard card: `MODULE 999`.
- Navigation group: **Help & Documentation**.
- Route: `#user-guide`.
- Access model: all authenticated ProjectPulse users.
- Rollback attempted on final deployment: `No`.
- Rollback result: `not-required`.

### Change boundaries

- API changed: `No`.
- Database changed: `No`.
- Entra changed: `No`.
- Source changed during final deployment: `No`.

### Deployment evidence directory

`/home/ahmed/az12d4/module-999-final-deploy-20260718T012956Z`
