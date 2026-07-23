# AZ-05C1B Conditional Access blocked AzCopy device-code authentication

Date: 2026-07-12

## Summary

The PostgreSQL 13 source export completed successfully, including the custom-format dump, inventory files, manifest, and SHA-256 verification. AzCopy installation also succeeded.

The upload did not start because the US Signal Microsoft Entra tenant rejected AzCopy's OAuth device-code authentication flow. The browser reported that sign-in succeeded but did not meet the criteria to access the resource, which is consistent with a Conditional Access restriction on the device-code flow, source location, device state, or authentication context.

## Impact

- No Azure Blob objects were uploaded by the failed attempt.
- The local verified export package remains available at:
  `/home/opc/project-health-dashboard-migration/exports/postgresql13-20260712T023119Z`
- The source database was not changed.
- The Azure PostgreSQL primary was not changed.
- No storage account key was enabled or used.

## Decision

Do not request a Conditional Access bypass and do not enable shared-key authorization on the storage account.

Use a short-lived user delegation SAS generated from the existing authenticated Azure Cloud Shell session. The SAS is:

- Signed with Microsoft Entra credentials.
- Scoped to the existing `database-exports` container.
- HTTPS-only.
- Limited to create, list, read, and write permissions.
- Valid for 60 minutes by default.
- Entered into the Oracle Linux source host through a hidden prompt.
- Never committed to GitHub or written to a persistent file by the canonical scripts.

## Canonical scripts

1. `deployment/azure/scripts/az05c1c-generate-user-delegation-sas-cloudshell.sh`
2. `deployment/azure/scripts/az05c1d-source-upload-with-user-delegation-sas.sh`

## Security notes

A user delegation SAS is a bearer credential. Anyone possessing it can use the granted permissions until expiration. It must not be placed in chat, email, tickets, screenshots, shell-history commands, GitHub, logs, or configuration files.

The token is allowed to expire automatically. Revoking all user delegation keys on the storage account is possible, but that action invalidates every user delegation SAS associated with the account and therefore is not performed automatically.
