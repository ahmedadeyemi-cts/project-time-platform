# AZ-05C1 AzCopy device authentication timeout

Date: 2026-07-12

## Result

The Oracle Linux source host successfully created the initial PostgreSQL 13 migration export package for database `ProjectPulse`.

Completed before the stop:

- PostgreSQL 13.23 source version confirmed
- Custom-format dump created
- Dump size approximately 3.2 MiB
- Database metadata captured
- Extension, schema, table, row-count, and sequence inventories captured
- Global objects exported without role passwords
- Source Git checkpoint metadata recorded
- `SHA256SUMS` generated and validated successfully
- AzCopy 10.32.4 installed under the source user's home directory

Export directory:

`/home/opc/project-health-dashboard-migration/exports/postgresql13-20260712T023119Z`

Planned destination:

`stphdtest7825cc/database-exports/source-postgresql13/20260712T023119Z`

## Failure

AzCopy device-code authentication was not completed before the credential deadline. The preliminary `azcopy list` command ended with a device-code timeout. Upload did not begin.

The expired device code is not reusable and is not a persistent credential.

## Recovery

Use:

`deployment/azure/scripts/az05c1b-source-postgresql-upload-continuation.sh`

The continuation:

- Locates the latest completed export package or accepts `EXPORT_DIR`
- Revalidates `SHA256SUMS`
- Reads the destination from `manifest.json`
- Requests a new device code
- Uploads the existing package without recreating the dump
- Lists the uploaded Blob objects for validation

Do not delete the local package until Azure restore and validation are complete.
