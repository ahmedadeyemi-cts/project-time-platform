# AZ-05B5 PostgreSQL Primary Ready

Date: 2026-07-12

## Result

The Azure Database for PostgreSQL Flexible Server primary foundation is complete and validated.

## Deployed server

- Server: `pg-phd-test-w3-7825cc`
- FQDN: `pg-phd-test-w3-7825cc.postgres.database.azure.com`
- Region: West US 3
- PostgreSQL version: 16
- Tier: General Purpose
- SKU: `Standard_D2ds_v4`
- Storage: 128 GiB Premium SSD
- Storage autogrow: enabled
- Backup retention: 35 days
- Geo-redundant backup: enabled
- Public network access: disabled
- Delegated subnet: `vnet-phd-test-westus3/snet-postgresql`
- Private DNS zone: `phd-test.postgres.database.azure.com`

## High availability

- Desired mode: `ZoneRedundant`
- Actual mode: `SameZone`
- HA state: `Healthy`
- Primary zone: `1`
- Standby zone: `1`

Azure used the documented same-zone fallback because cross-zone capacity was unavailable during provisioning. This protects against primary-node and service failure but does not protect against a full availability-zone outage. The East US cross-region replica remains part of the disaster-recovery design and will be created after the source data import is complete.

## Application database

- Database: `project_health_dashboard`
- Charset: `UTF8`
- Collation: `en_US.utf8`

## Configuration completed

- `pgcrypto` added to the Azure extension allow-list
- Database connection metadata stored in both regional Key Vaults
- PostgreSQL administrator password remains only in Key Vault
- Non-secret configuration written to:
  - `$HOME/project-health-dashboard-azure/config/postgresql-primary.env`
- Execution log written to:
  - `$HOME/project-health-dashboard-azure/logs/az05b5-postgresql-primary-continuation-20260712T022240Z.log`

## Next phase

AZ-05C performs the initial PostgreSQL 13 migration seed:

1. Export the source PostgreSQL 13 database in custom archive format.
2. Capture extensions, schemas, table counts, sequence values, and checksums.
3. Upload the export package to the private `database-exports` Blob container using Microsoft Entra authorization.
4. Restore from a temporary private Azure migration host.
5. Validate row counts, sequences, extensions, and application queries.

The East US read replica must not be created until the initial import and validation are complete.
