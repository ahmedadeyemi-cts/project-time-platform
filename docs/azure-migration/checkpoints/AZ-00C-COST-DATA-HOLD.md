# AZ-00C Cost Data Hold

## Status

Accepted on 2026-07-12.

## Result

The test-subscription cost checkpoint completed successfully but Azure Cost Management returned no month-to-date actual-cost rows and no forecast rows.

The decision is therefore:

- `ACTUAL_COST_DATA=unavailable`
- `FORECAST_COST_DATA=unavailable`
- `COST_DECISION=HOLD_NO_COST_DATA`
- `ROCKY_RESTORE_VM_APPROVAL=HOLD`

## Confirmed persistent cost drivers

- PostgreSQL Flexible Server `pg-phd-test-w3-7825cc`
  - PostgreSQL 16
  - General Purpose
  - `Standard_D2ds_v4`
  - SameZone HA
  - 128 GiB storage
- Premium ACR `acrphdtest7825cc`
  - Zone redundancy enabled
  - Two replications
- Two NAT Gateways
- Four Standard static public IP addresses
- Six private endpoints
- Regional Log Analytics and Application Insights
- RA-GZRS storage
- Regional Key Vaults

## Guardrail

No Rocky Linux restore VM, East US PostgreSQL replica, Application Gateway WAF_v2, or Azure Front Door Premium may be created in the test subscription until actual or forecast cost data is available and reviewed against the USD 200 monthly ceiling.

## Existing migration artifacts

The PostgreSQL 13 source export remains safely stored in Azure Blob Storage under:

`database-exports/source-postgresql13/20260712T023119Z`

The local source export must also be retained until restore validation completes.

## Next action

Rerun `deployment/azure/scripts/az00c-test-subscription-cost-check.sh` after Azure billing data has posted. Only proceed with AZ-05C2A when the script returns an explicit approval decision.
