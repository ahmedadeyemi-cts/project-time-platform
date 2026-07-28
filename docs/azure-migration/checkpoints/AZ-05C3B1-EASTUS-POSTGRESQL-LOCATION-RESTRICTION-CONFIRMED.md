# AZ-05C3B1 — East US PostgreSQL Location Restriction Confirmed

**Date:** 2026-07-12 UTC

## Outcome

The East US PostgreSQL replica creation request was rejected before provisioning with:

`The location is restricted from performing this operation.`

No replica resource was created, no submission state file was written, and no replica billing started.

## Diagnostic evidence

The read-only diagnostic confirmed:

- planned replica exists: `false`
- replica submission state file present: `false`
- East US fast-provisioning compatibility matches: `1`
- exact supported configuration:
  - SKU: `Standard_D2ds_v4`
  - tier: `GeneralPurpose`
  - PostgreSQL: `16`
  - storage: `128 GiB`
- capability root reason:

  `Provisioning is restricted in this region. Please choose a different region. For exceptions to this rule please open a support request with Issue type of 'Service and subscription limits'.`

- current activity-log match count: `0`
- activity-log event: not yet available
- decision: `CONTROL_PLANE_RESTRICTION_REQUIRES_SUPPORT_REVIEW`
- retry creation now: `false`

## Interpretation

East US advertises the required PostgreSQL configuration, but this subscription is blocked from provisioning PostgreSQL Flexible Server in the region. This is a subscription/region access restriction, not a SKU incompatibility.

## Required next action

Open an Azure support request using issue type **Service and subscription limits** and request East US PostgreSQL Flexible Server provisioning access for subscription:

`cd32baeb-7b71-4bc0-8ea3-9f23a50903fe`

Do not retry replica creation until Azure confirms the restriction has been removed.

## Current architecture state

- West US 3 PostgreSQL primary: Ready
- imported application database: validated
- East US read replica: blocked by regional provisioning restriction
- migration VM: deallocated
- temporary upload role: removed
