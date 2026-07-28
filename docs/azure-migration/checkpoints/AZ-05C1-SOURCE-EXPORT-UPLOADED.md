# AZ-05C1 Checkpoint — Source PostgreSQL Export Uploaded

Date: 2026-07-12

## Result

The initial PostgreSQL 13 migration seed was exported from the active Oracle Cloud source host and uploaded to Azure Blob Storage.

Azure destination:

- Storage account: `stphdtest7825cc`
- Container: `database-exports`
- Prefix: `source-postgresql13/20260712T023119Z`

## Verified artifacts

The Azure Blob listing confirmed all 15 expected objects:

- `ProjectPulse-pg13-20260712T023119Z.dump` — 3,341,746 bytes
- `ProjectPulse-pg13-20260712T023119Z.dump.log`
- `ProjectPulse-pg13-20260712T023119Z.toc.txt`
- `SHA256SUMS`
- `database-metadata.csv`
- `extensions.csv`
- `manifest.json`
- `pg-dump-version.txt`
- `pg-restore-version.txt`
- `postgresql-globals-no-passwords-20260712T023119Z.sql`
- `row-counts.csv`
- `schemas.csv`
- `sequences.csv`
- `server-version.txt`
- `tables.csv`

The source-side checksum verification completed before upload.

## Authentication note

Microsoft Entra Conditional Access blocked AzCopy device-code authentication. The upload was completed by using a short-lived, HTTPS-only user-delegation SAS generated from Azure Cloud Shell. No storage account key was enabled or used, and no SAS token was committed to GitHub.

## Migration state

- This package is an initial test-migration seed.
- The Oracle Cloud source application remains active and writable.
- A final export is still required during the eventual cutover write freeze.
- The local source export must remain available until Azure restore and validation are complete.

## Next phase

AZ-05C2 creates a temporary private restore runner in the West US 3 management subnet. The runner will have no public IP, will use managed identity for Blob and Key Vault access, and will connect privately to `pg-phd-test-w3-7825cc.postgres.database.azure.com`.
