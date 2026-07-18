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

## Source state

This source phase builds and validates Module 999, commits it, and pushes it to its
dedicated feature branch. It does not deploy Azure or change the API, database, or
Entra configuration.

## Validation-marker correction

The first guarded web deployment successfully built, started, and exposed every
Module 999 and preserved-module marker. Post-deployment validation then detected
a retired user-interface label because that literal phrase appeared only in a
user-guide note explaining that the old control was unavailable. The deployment
correctly rolled back.

The guide now describes the current direct-entry and personal-default behavior
without embedding the retired UI label, keeping bundle validation unambiguous.
