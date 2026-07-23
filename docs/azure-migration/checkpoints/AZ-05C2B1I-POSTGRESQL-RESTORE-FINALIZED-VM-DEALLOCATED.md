# AZ-05C2B1I — PostgreSQL Restore Finalized and Runner Deallocated

**Date:** 2026-07-12

## Restore outcome

The clean PostgreSQL initial-seed restore retry completed successfully.

- Run Command: `phdrestoreseedretry20260712t184644z`
- Result prefix: `restore-results/retry-20260712T184644Z`
- Execution state: `Succeeded`
- Exit code: `0`
- Success marker: `POSTGRESQL INITIAL SEED RESTORE RETRY VALIDATION PASSED`

## Validation evidence

Uploaded nonsecret evidence was independently verified through the runner managed identity.

- Evidence objects: `12`
- Required evidence objects: `12`
- Validation summary: `PASSED`
- Comparison status: `PASSED`
- Comparison errors: `0`
- Comparison warnings: `0`
- Schemas: `1`
- Tables: `170`
- Extensions: `2`
- Sequences: `0`

The warnings emitted by `ANALYZE` for Azure-managed PostgreSQL system catalogs did not affect the application-data validation result.

## Security cleanup

The temporary container-scoped `Storage Blob Data Contributor` assignment used only to upload restore evidence was removed.

- Role action: `deleted`
- Remaining matching assignments: `0`

## Cost cleanup

The temporary Rocky Linux migration runner was deallocated after evidence verification.

- VM: `vm-phd-test-db-migrate-eus`
- Resource group: `rg-project-health-dashboard-test-migration-eastus`
- Provisioning state: `Succeeded`
- Power state: `VM deallocated`

Compute billing for the VM is stopped while it remains deallocated. Its disks and other retained resources remain provisioned until explicitly deleted.

## Database migration state

The Azure Database for PostgreSQL Flexible Server primary now contains the verified initial seed from the source PostgreSQL 13 database.

The source application remains active. A final write-freeze export and delta/cutover procedure are still required before production cutover.

## Next phase

The import prerequisite for the East US asynchronous PostgreSQL replica is satisfied.

Before creating the billable replica, run a read-only preflight to validate:

1. the West US 3 primary is `Ready`;
2. the imported database remains present;
3. the planned East US replica does not already exist;
4. East US PostgreSQL SKU availability;
5. the delegated East US PostgreSQL subnet;
6. the PostgreSQL private DNS VNet link;
7. current replica topology and source-server eligibility.
