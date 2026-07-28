# AZ-05B2 PostgreSQL SKU Schema Mismatch

Date: 2026-07-11 UTC

## Summary

The clean PostgreSQL primary deployment stopped during SKU discovery before reading database credentials or creating an Azure Database for PostgreSQL Flexible Server.

## Observed behavior

Both West US 3 and East US `az postgres flexible-server list-skus` commands completed, but the parser found no common two-vCore SKU strings.

The deployment ended with:

```text
No common two-vCore SKU was found.
ERROR: SKU discovery failed.
```

## Impact

- No PostgreSQL server was created.
- No application database was created.
- No network resource was changed.
- No DNS resource was changed.
- No Key Vault secret was read or modified by this attempt because execution stopped before the credential-loading stage.

## Cause

The installed Azure CLI returns a `list-skus` JSON structure that does not expose SKU names in the scalar shape assumed by the AZ-05B2 parser.

## Corrective action

Run the read-only `az05b2a-postgresql-sku-discovery.sh` script. It captures the raw regional responses, displays Azure CLI table output, inventories dictionary key shapes, and records relevant scalar paths. Use that evidence to build a subscription-specific but reusable SKU selector.

Do not guess a SKU or bypass regional availability checks because the future East US replica must use a supported configuration.
