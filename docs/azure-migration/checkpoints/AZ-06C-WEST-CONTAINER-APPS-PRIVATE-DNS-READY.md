# AZ-06C — West Container Apps Private DNS Ready

Date: 2026-07-12

## Purpose

Prepare the private DNS configuration for the internal West US 3 Azure Container Apps environment.

## Planned configuration

- Container Apps environment: `cae-phd-test-westus3`
- Generated default domain: read live from Azure
- Internal static IP: read live from Azure
- Private DNS zone: exact generated default domain
- Wildcard A record: `*` pointing to the environment internal static IP
- West VNet link: `vnet-phd-test-westus3`
- East VNet link: `vnet-phd-test-eastus`
- VNet registration: disabled

## Safety

- Guard variable required: `PHD_CREATE_BILLABLE_WEST_ACA_DNS=YES`
- The script validates that the environment is internal and in `Succeeded` state.
- The script does not deploy container images or container apps.
- The script does not retry the East PostgreSQL replica.
- The script does not create public or Cloudflare DNS records.

## Next action

Run `deployment/azure/scripts/az06c-configure-west-container-apps-private-dns.sh` from Azure Cloud Shell.
