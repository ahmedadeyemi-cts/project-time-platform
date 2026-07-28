# AZ-05B PostgreSQL CLI Compatibility Incident

Date: 2026-07-12

## Result

The initial AZ-05B execution stopped before creating an Azure Database for PostgreSQL Flexible Server.

## Symptoms

1. The common-SKU selection printed no selected value after reporting that no preferred SKU was found.
2. `az postgres flexible-server create` rejected `--database-name` because the installed CLI applies that argument only to elastic clusters when `--node-count` is present.

## Azure state after the stop

- No PostgreSQL Flexible Server was created.
- No application database was created.
- The PostgreSQL administrator password was generated and stored in both regional Key Vaults.
- Existing VNets, delegated subnets, Key Vaults, and private DNS were unchanged.

## Root causes

- The SKU parser looked only at values under selected JSON keys. The current `list-skus` response can expose SKU strings elsewhere in the response.
- The shell assignment did not explicitly validate a nonempty SKU before continuing.
- The CLI contract for a standard Flexible Server requires creating the application database separately with `az postgres flexible-server db create`.

## Correction

Use:

`deployment/azure/scripts/az05b1-postgresql-primary-repair.sh`

The repair script:

- Recursively discovers all SKU-formatted strings in both regional `list-skus` responses.
- Selects a common `Standard_D2*` SKU.
- Hard-fails on SKU-selection failure or an empty result.
- Reuses the administrator password already stored in Key Vault.
- Creates the PostgreSQL server without `--database-name`.
- Creates `project_health_dashboard` separately after server readiness.
- Continues HA, autogrow, backup, private networking, extension, secret, and validation configuration.

## Safety

Do not delete or rotate the administrator-password secret before the repair script runs. Do not create the East US replica until the source database has been imported and validated.
