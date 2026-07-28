# AZ-05C2A5: Invalid Quota Resource Name and Fast Path

## Status

Open — VM allocation remains blocked until a valid East US VM-family quota is available.

## Date

2026-07-12

## What happened

The Azure CLI quota extension was installed successfully at version `1.0.0`.

A quota lookup using:

`StandardDaldsv7Family`

returned:

`InvalidResourceName: Name StandardDaldsv7Family is not valid resource name.`

The recent quota-request list returned no request records.

## Impact

- No VM was created.
- No operating-system installation started.
- No new compute billing began.
- The East US network, NAT Gateway, VNet peering, PostgreSQL private DNS link, and Rocky Linux 10 image prerequisites remain ready.

## Important Azure behavior

Azure enforces both regional vCPU quota and VM-family vCPU quota before VM allocation. A VM deployment cannot begin and later be made compliant retroactively. The required quota must already be available when the VM deployment preflight runs.

## Fastest approved path

1. Use Azure Portal **Quotas > Compute** or the VM size-selection page to request quota for a small, available East US VM family. This avoids guessing the Microsoft.Quota resource identifier.
2. Request the minimum practical limit, normally two vCPUs for a two-vCPU restore runner.
3. While approval is pending, continue only non-compute preparation: deployment scripts, validation scripts, migration package checks, and resource-state documentation.
4. As soon as quota is approved, create the private Rocky Linux 10 runner, perform the restore and validation, and delete or deallocate it promptly.
5. A second fast path is to identify another East US VM family that already has at least two available vCPUs and deploy the runner on that family after validating Rocky Linux 10 x86-64-v3 compatibility.

## Decision

Do not attempt to create the VM before quota is available. Do not weaken PostgreSQL private-access controls to bypass the VM requirement. Use the Azure Portal quota workflow or a currently deployable compatible VM family.
