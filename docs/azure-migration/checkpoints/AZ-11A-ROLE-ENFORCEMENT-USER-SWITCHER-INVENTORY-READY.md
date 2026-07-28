# AZ-11A — Role Enforcement and User Switcher Inventory Ready

Date prepared: 2026-07-13

## Purpose

Inventory the existing authentication, administrator View-As, role, permission, session, security, and audit implementation before changing application behavior.

## Existing evidence

The current source already contains:

- Session validation middleware
- Effective-user View-As context handling
- An administrator-facing global View-As selector
- Read-only browser-side blocking while View-As is active
- Effective-session visibility and telemetry UI

The inventory is needed to determine which controls are currently enforced by the API and which exist only in the browser.

## Safety

- No Azure resource is changed.
- No database schema or data is changed.
- No application image is rebuilt or deployed.
- PR #11 and its source branch are not modified.
- The Oracle VM is not required.
- The script validates that PR #11 remains at commit `abf45bf824747767282f68fa5bd50909f9751eb0` before inspecting source.

## Canonical script

`deployment/azure/scripts/az11a-role-enforcement-user-switcher-source-inventory.sh`

## Expected terminal result

`ROLE_ENFORCEMENT_USER_SWITCHER_INVENTORY_RESULT=READY`

## Planned implementation after inventory

1. Define canonical roles and permissions.
2. Enforce authorization on the server for every protected route.
3. Preserve actor identity separately from effective View-As identity.
4. Keep View-As read-only unless a future explicitly approved support workflow requires otherwise.
5. Restrict user switching to authorized administrators.
6. Add durable audit events for enter, change, and exit actions.
7. Make navigation and controls reflect the effective user’s permissions without treating frontend hiding as security.
8. Validate access-denied behavior using multiple test users.
