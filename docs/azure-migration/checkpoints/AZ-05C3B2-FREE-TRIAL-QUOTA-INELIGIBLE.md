# AZ-05C3B2 — Free Trial Subscription Ineligible for East US Quota Exception

**Date:** 2026-07-12

## Outcome

The East US PostgreSQL Flexible Server capability response confirms that the planned configuration is technically supported:

- SKU: `Standard_D2ds_v4`
- Tier: `GeneralPurpose`
- PostgreSQL version: `16`
- Storage: `128 GiB`
- Compatible fast-provisioning match count: `1`

The actual replica create request was rejected with:

`The location is restricted from performing this operation.`

The Azure support workflow then returned:

`Your free trial subscription isn't eligible for a quota increase. To request a quota increase, first upgrade to a Pay-As-You-Go subscription.`

## Current state

- East US replica exists: no
- Replica creation state file: absent
- Replica billing started: no
- Retry creation now: no
- Technical regional capability: confirmed
- Subscription eligibility for regional exception: blocked by Free Trial offer type

## Required prerequisite

Upgrade subscription `cd32baeb-7b71-4bc0-8ea3-9f23a50903fe` from Azure Free Trial to Pay-As-You-Go before reopening the **Service and subscription limits** support request.

Upgrading the subscription does not itself guarantee that East US access is automatically enabled. It makes the subscription eligible to request the exception from Microsoft.

## Cost implications

After upgrade:

- existing resources remain in the same subscription;
- unused free-account credit remains available only for the original 30-day credit window;
- usage beyond remaining credit or after credit expiry is charged to the configured payment method;
- Azure budgets and alerts notify but do not enforce a hard spending stop.

## Decision options

1. **Upgrade the test subscription to Pay-As-You-Go** and reopen the regional-access request.
2. **Defer the East US replica** and continue application work against the validated West US 3 primary.
3. **Redesign the secondary region**, which requires new regional capability, policy, network, and cost validation.

The recommended path for the existing architecture is option 1, subject to explicit acceptance of Pay-As-You-Go billing exposure.
