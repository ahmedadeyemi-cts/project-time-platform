# AZ-05C3B3 — Pay-As-You-Go Upgrade Confirmed

**Date:** 2026-07-12

## Evidence

The Azure portal displayed:

`You've upgraded. Stay on track with your Azure costs.`

This confirms that subscription `cd32baeb-7b71-4bc0-8ea3-9f23a50903fe` completed the upgrade from the Azure Free Trial offer to a paid Pay-As-You-Go subscription.

## Current impact

- the subscription is no longer blocked by Free Trial quota-request ineligibility;
- the East US PostgreSQL provisioning restriction still requires a Service and subscription limits support request;
- the East US PostgreSQL replica has not been created;
- no replica billing has started;
- the migration VM remains deallocated;
- the guarded replica creation script must not be rerun until Azure approves East US PostgreSQL provisioning access.

## Next action

Reopen the Azure support request for Azure Database for PostgreSQL Flexible Server East US provisioning access using issue type `Service and subscription limits`.
