# AZ-05B3 PostgreSQL Same-Zone HA Fallback

Date: 2026-07-11/12 UTC

## Result

AZ-05B3 stopped before creating the PostgreSQL Flexible Server.

Azure returned:

```text
This location has a single availability zone. To proceed, set --allow-same-zone.
```

No PostgreSQL server or database was created. The existing administrator password remained in Key Vault.

## Decision

Continue using West US 3 as the primary region and retain high availability by adding:

```text
--zonal-resiliency Enabled
--allow-same-zone
```

Azure will first attempt zone-redundant HA. When zonal capacity is unavailable, it is permitted to deploy the managed standby in the same availability zone as the primary. This preserves automatic failover for node/service failures but does not protect the regional primary from a full availability-zone outage while the mode remains SameZone.

The later East US cross-region replica remains the regional disaster-recovery control. It will be created after source database import and validation.

## Validation requirement

The canonical deployment must accept either:

- `ZoneRedundant`, when Azure can provision separate zones; or
- `SameZone`, when the documented fallback is used.

The actual HA mode and state must be written to the non-secret migration configuration and execution history. Zone-redundant HA remains the desired final state.