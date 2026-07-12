# AZ-05C2A3: East US Management Subnet Missing NAT Attachment

## Status

Resolved by continuation script on 2026-07-12.

## What happened

The East US Rocky Linux 10 restore-runner preflight validated that the existing East US management subnet was present and healthy, but the subnet had no NAT Gateway association:

- VNet: `vnet-phd-test-eastus`
- Subnet: `snet-management`
- Prefix: `10.40.7.0/24`
- Provisioning state: `Succeeded`
- Expected NAT Gateway: `nat-phd-test-aca-eastus`
- Observed NAT Gateway association: empty

The preflight stopped before image, peering, DNS, SKU, quota, NIC, or VM deployment actions.

## Impact

- No virtual machine was created.
- No network interface was created in East US.
- No public IP was created.
- No new billable Azure resource was created.
- The existing East US NAT Gateway remains the intended outbound-egress resource.

## Resolution

Added:

`deployment/azure/scripts/az05c2a3b-eastus-management-nat-continuation.sh`

The continuation:

1. Verifies the existing East US NAT Gateway and its provisioning state.
2. Refuses to replace a different NAT Gateway association if one is present.
3. Attaches the existing `nat-phd-test-aca-eastus` resource when the subnet has no NAT Gateway.
4. Validates the final subnet provisioning state and exact NAT Gateway resource ID.
5. Creates no new Azure resource.

After the continuation succeeds, rerun:

`deployment/azure/scripts/az05c2a3-eastus-rocky10-restore-runner-preflight.sh`

## Required success marker

`EASTUS MANAGEMENT SUBNET NAT ATTACHMENT READY`
