# AZ-05B4 Partial Checkpoint — PostgreSQL Server Created

Date: 2026-07-11 Pacific / 2026-07-12 UTC

## Confirmed completed

- PostgreSQL Flexible Server `pg-phd-test-w3-7825cc` was created in West US 3.
- PostgreSQL version: 16.
- SKU requested: `Standard_D2ds_v4`.
- Storage requested: 128 GiB Premium SSD.
- Storage autogrow requested: enabled.
- Backup retention requested: 35 days.
- Geo-redundant backup requested: enabled.
- High availability requested with zonal resiliency and same-zone fallback allowed.
- Private delegated subnet and private DNS were supplied during creation.

## Script stop

The server creation and readiness wait completed. The next database-creation command stopped because it supplied a charset without a collation:

```text
ERROR: charset and collation have to be input together.
```

## Pending completion

- Create database `project_health_dashboard` using server defaults.
- Validate the resulting UTF8 charset and reported collation.
- Save non-secret host/database metadata in both Key Vaults.
- Configure `azure.extensions=PGCRYPTO`.
- Attempt enhanced database activity metrics.
- Capture actual HA mode, state, and availability zones.
- Write `postgresql-primary.env`.

## Continuation

Run:

`deployment/azure/scripts/az05b5-postgresql-primary-continuation.sh`

Do not delete or recreate the server solely because of the database command failure.
