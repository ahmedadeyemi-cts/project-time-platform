# ProjectPulse 050 Critical Launch Blocker Bundle

## Purpose

This module addresses the first launch-blocker bundle from the principal product review.

## Covered items

- PP-C1: Dev-login/auth-shortcut routes must not mint sessions in production.
- PP-C2: Dangerous approval/time/accounting/admin/profile routes must require a valid session.
- PP-C6: Active deployment writes a release manifest with git commit and API DLL checksum.
- PP-C7: A repeatable backup/restore smoke harness is added so DR checks are not informal.

## Not fully closed by this bundle

The following require logged-in, role-specific workflow testing and/or deeper data-model enforcement after this baseline hardening:

- PP-C3: Engineers must not edit approved/reconciled/locked time.
- PP-C4: Engineers must be able to draft-save time without admin permissions.
- PP-C5: UI day-submit undefined-variable issue must be confirmed by browser workflow testing.

## Required browser validation

1. Sign in as Admin and confirm the app loads.
2. Sign in as Engineer and confirm draft-save works.
3. Submit a day as Engineer.
4. Approve/reconcile/lock the day as Manager/Admin.
5. Return as Engineer and confirm the locked day cannot be changed.
6. Confirm no unauthenticated approval/time/accounting/admin route returns success.
7. Confirm `/opt/project-time-platform/app/published/api/projectpulse-release-manifest.json` exists after deploy.
