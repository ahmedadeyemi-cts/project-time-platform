# AZ-05C2A4: Microsoft.Quota Resource Provider Not Registered

## Status

Resolved by registration continuation prepared on 2026-07-12.

## What happened

The East US direct compute quota preflight successfully collected 1,356 VM SKU records, then stopped when querying the Microsoft Quota REST API.

Azure returned:

```text
MissingRegistrationForResourceProvider
The subscription is not in registered state for the resource provider: Microsoft.Quota.
```

## Impact

- No virtual machine was created.
- No NIC or public IP was created in East US.
- No billable compute resource was started.
- The East US management subnet NAT attachment, VNet peering, and PostgreSQL private DNS link remain healthy.

## Root cause

The subscription had not previously registered the `Microsoft.Quota` resource provider. The quota REST endpoints cannot be used until this provider is registered for the subscription.

## Resolution

Added:

`deployment/azure/scripts/az05c2a4b-register-microsoft-quota-provider.sh`

The continuation:

- checks the current registration state;
- registers only `Microsoft.Quota` when needed;
- waits for the provider to reach `Registered`;
- validates provider metadata;
- creates no VM or other billable Azure resource.

## Required success marker

```text
MICROSOFT QUOTA RESOURCE PROVIDER READY
```

After registration, rerun:

`deployment/azure/scripts/az05c2a4-eastus-quota-rest-preflight.sh`
