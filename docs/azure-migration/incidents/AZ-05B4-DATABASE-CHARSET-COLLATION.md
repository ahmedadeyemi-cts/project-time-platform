# AZ-05B4 Database Charset/Collation Stop

Date: 2026-07-11 Pacific / 2026-07-12 UTC

## Result

The PostgreSQL Flexible Server was created successfully:

- Server: `pg-phd-test-w3-7825cc`
- Region: West US 3
- PostgreSQL: 16
- SKU: `Standard_D2ds_v4`
- Storage: 128 GiB
- HA request: zonal resiliency with same-zone fallback allowed
- Private delegated subnet and private DNS configured during server creation

The script then stopped while creating the application database because it supplied `--charset UTF8` without a matching `--collation` value. The Azure CLI returned:

```text
ERROR: charset and collation have to be input together.
```

## Impact

- The PostgreSQL server exists and is billable.
- The managed HA standby was provisioned as part of the server operation.
- The application database `project_health_dashboard` was not created during this attempt.
- Post-creation secret metadata, parameter configuration, final validation, and generated configuration file were not completed.
- No server deletion or recreation is required.

## Correction

Use AZ-05B5 as a continuation script. It:

1. Confirms the existing server is ready.
2. Creates the application database using PostgreSQL server defaults, omitting both `--charset` and `--collation`.
3. Confirms the resulting database charset is UTF8.
4. Saves non-secret connection metadata to both Key Vaults.
5. Configures the `PGCRYPTO` extension allow-list.
6. Validates compute, storage, backup, HA, networking, and database properties.
7. Writes the non-secret PostgreSQL configuration file.

## Canonical continuation

`deployment/azure/scripts/az05b5-postgresql-primary-continuation.sh`

Do not rerun AZ-05B4 solely to correct this database command. AZ-05B5 safely continues from the existing server.
