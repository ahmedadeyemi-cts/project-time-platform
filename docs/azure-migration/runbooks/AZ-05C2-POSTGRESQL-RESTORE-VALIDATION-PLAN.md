# AZ-05C2 PostgreSQL Restore and Validation Plan

## Status

Prepared while the test subscription is under `HOLD_NO_COST_DATA`.

No restore-runner VM, East US PostgreSQL replica, Application Gateway, or Front Door resource may be created until AZ-00C approves temporary compute under the USD 200 monthly ceiling.

## Objective

Restore the verified PostgreSQL 13 source archive into the private Azure Database for PostgreSQL Flexible Server primary and prove that the initial test seed is complete and internally consistent.

## Source package

- Storage account: `stphdtest7825cc`
- Container: `database-exports`
- Prefix: `source-postgresql13/20260712T023119Z`
- Archive: `ProjectPulse-pg13-20260712T023119Z.dump`
- Archive bytes: `3341746`
- Supporting artifacts: SHA256SUMS, manifest, schemas, extensions, tables, exact row counts, sequences, globals without passwords, dump log, and restore TOC

## Target

- Server: `pg-phd-test-w3-7825cc`
- Database: `project_health_dashboard`
- PostgreSQL: 16
- Network access: private only
- Administrative secret: `postgres-admin-password` in `kv-phd-t-w3-7825cc`

## Execution gates

1. AZ-00C must return usable actual or forecast cost data.
2. The monthly forecast must remain within the USD 200 ceiling with adequate headroom.
3. The temporary Rocky Linux 10 VM must be explicitly approved.
4. The East US PostgreSQL replica must remain absent until restore validation passes.
5. The source export package must remain unchanged and checksum-valid.

## Temporary restore runner

- Operating system: Rocky Linux 10.x x86-64
- Official publisher: `resf`
- Public IP: none
- Administration: Azure Run Command
- Authentication: system-assigned managed identity
- Blob download role: Storage Blob Data Reader
- Temporary result-upload role: Storage Blob Data Contributor at container scope only
- Key Vault role: Key Vault Secrets User
- Required final state: deallocated immediately after validation, then deleted after logs are preserved

## Restore procedure

1. Start the approved private Rocky Linux 10 restore runner.
2. Install the Rocky Linux PostgreSQL 16 client, AzCopy, curl, jq, and DNS utilities.
3. Download the complete source package by using the VM managed identity.
4. Validate every package file against `SHA256SUMS`.
5. Confirm exactly one custom-format `.dump` archive exists.
6. Run `pg_restore --list` against the archive.
7. Retrieve the PostgreSQL administrator password from Key Vault through the VM managed identity; never print or persist it.
8. Require SSL for all PostgreSQL connections.
9. Confirm the target database contains no user tables, views, materialized views, foreign tables, or sequences.
10. Restore using `pg_restore --no-owner --no-privileges --exit-on-error --verbose`.
11. Run `ANALYZE` after the restore.

## Validation procedure

The restore does not pass until all of the following are verified:

- Dump SHA-256 matches the source manifest.
- Schema-name inventory matches.
- Table-name inventory matches.
- Exact row counts match for every table.
- Extension-name inventory matches; version differences are recorded for review.
- Sequence inventory and values match.
- PostgreSQL connectivity uses the private FQDN and SSL.
- No source or Azure password appears in logs or result files.

## Result preservation

Upload non-secret restore evidence to:

`database-exports/restore-results/<restore-timestamp>`

The result package must include:

- restore log
- checksum-verification output
- target schema inventory
- target extension inventory
- target table inventory
- target exact row counts
- target sequence inventory
- validation summary
- result manifest

## Cost controls

- The restore runner is started only when the restore is ready to execute.
- A cleanup handler must deallocate the VM even when restore or validation fails.
- The temporary Blob Contributor assignment is removed after result upload.
- The VM, NIC, OS disk, and remaining role assignments are deleted after evidence is confirmed in Blob Storage.

## Initial seed versus final cutover

This restore is an initial test seed. The Oracle Linux source environment remains active. Before production cutover, the project still requires:

1. application write freeze;
2. final PostgreSQL export;
3. final restore or controlled delta procedure;
4. final row-count and sequence validation;
5. application connection-string switch;
6. smoke tests and rollback decision;
7. creation of production resilience resources in the paid production subscription.
