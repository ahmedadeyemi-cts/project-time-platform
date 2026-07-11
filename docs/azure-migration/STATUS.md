# Azure Migration Status

Last updated: 2026-07-11

## Source environment

- Source cloud: Oracle Cloud Infrastructure
- Source OS: Oracle Linux Server 9.7
- Source compute: 2 vCPU, approximately 945 MiB RAM
- Source database: PostgreSQL 13.23
- Source database size at discovery: approximately 31 MB
- Source application repository HEAD at discovery: `5a221da29cdfc1134e5d603175b311ff97658b67`
- Current public hostname: `projectpulse-test.onenecklab.com`
- Source remains the active environment until Azure cutover is validated.

## Azure subscription

- Subscription name: `Azure subscription 1`
- Subscription ID: `cd32baeb-7b71-4bc0-8ea3-9f23a50903fe`
- Tenant ID: `535941da-da72-4a8b-8378-983a54bec342`
- Primary region: `westus3`
- Secondary region: `eastus`

Subscription and tenant identifiers are not secrets, but they should still be treated as internal operational metadata.

## Resource groups

### Global

- `rg-project-health-dashboard-shared-global`
- `rg-project-health-dashboard-test-edge-global`

### West US 3

- `rg-project-health-dashboard-test-network-westus3`
- `rg-project-health-dashboard-test-app-westus3`
- `rg-project-health-dashboard-test-data-westus3`

### East US

- `rg-project-health-dashboard-test-network-eastus`
- `rg-project-health-dashboard-test-app-eastus`
- `rg-project-health-dashboard-test-data-eastus`

## Network foundation

### West US 3

- VNet: `vnet-phd-test-westus3`
- Address space: `10.30.0.0/16`
- Container Apps subnet: `10.30.0.0/23`
- PostgreSQL subnet: `10.30.4.0/24`
- Private endpoint subnet: `10.30.5.0/24`
- Azure Firewall reserved subnet: `10.30.6.0/26`
- Gateway reserved subnet: `10.30.6.64/27`
- Management subnet: `10.30.7.0/24`
- Application Gateway subnet: `10.30.8.0/24`

### East US

- VNet: `vnet-phd-test-eastus`
- Address space: `10.40.0.0/16`
- Container Apps subnet: `10.40.0.0/23`
- PostgreSQL subnet: `10.40.4.0/24`
- Private endpoint subnet: `10.40.5.0/24`
- Azure Firewall reserved subnet: `10.40.6.0/26`
- Gateway reserved subnet: `10.40.6.64/27`
- Management subnet: `10.40.7.0/24`
- Application Gateway subnet: `10.40.8.0/24`

Global VNet peering is configured in both directions. Route tables are associated but intentionally contain no forced-tunnel routes because Azure Firewall has not yet been deployed.

## Static public IP addresses

### Inbound regional ingress

- West US 3: `20.118.180.129`
  - Resource: `pip-phd-test-ingress-westus3`
- East US: `52.186.166.195`
  - Resource: `pip-phd-test-ingress-eastus`

These addresses are reserved for regional Application Gateway WAF_v2 instances. They are not serving application traffic yet.

### Outbound application egress

- West US 3: `20.106.109.188`
  - Resource: `pip-phd-test-egress-westus3`
  - NAT Gateway: `nat-phd-test-aca-westus3`
- East US: `20.88.160.79`
  - Resource: `pip-phd-test-egress-eastus`
  - NAT Gateway: `nat-phd-test-aca-eastus`

Both NAT Gateways are attached to their regional Container Apps infrastructure subnets.

## Planned temporary Cloudflare DNS records

Do not create these records until the corresponding Application Gateway is deployed and returning healthy HTTPS responses.

| Record | Type | Target | Initial proxy mode |
|---|---|---|---|
| `phd-west-test.onenecklab.com` | A | `20.118.180.129` | DNS only |
| `phd-east-test.onenecklab.com` | A | `52.186.166.195` | DNS only |

The final multi-region hostname will be `phd-test.onenecklab.com` and will use a CNAME to Azure Front Door Premium.

## Monitoring and identities

### West US 3

- Log Analytics: `log-phd-test-westus3`
- Application Insights: `appi-phd-test-westus3`
- Managed identity: `id-phd-test-app-westus3`

### East US

- Log Analytics: `log-phd-test-eastus`
- Application Insights: `appi-phd-test-eastus`
- Managed identity: `id-phd-test-app-eastus`

## Container registry

- Registry: `acrphdtest7825cc`
- SKU: Premium
- Primary location: West US 3
- Geo-replica: East US
- Zone redundancy: enabled in both regions
- Admin user: disabled
- Public access: temporarily enabled for image build and migration
- West private endpoint: `pe-phd-test-acr-westus3`
- East private endpoint: `pe-phd-test-acr-eastus`

Both application managed identities have `AcrPull` permission.

## Key Vaults

### West US 3

- Vault: `kv-phd-t-w3-7825cc`
- Private endpoint: `pe-phd-test-kv-westus3`

### East US

- Vault: `kv-phd-t-eus-7825cc`
- Private endpoint: `pe-phd-test-kv-eastus`

Both vaults use Azure RBAC and purge protection. Public access remains temporarily enabled for migration administration. Each regional application identity has `Key Vault Secrets User` on its regional vault.

## Private DNS zones

The following private DNS zones are created and linked to both VNets:

- `phd-test.postgres.database.azure.com`
- `privatelink.azurecr.io`
- `privatelink.blob.core.windows.net`
- `privatelink.file.core.windows.net`
- `privatelink.vaultcore.azure.net`
- `privatelink.westus3.azurecontainerapps.io`
- `privatelink.eastus.azurecontainerapps.io`

## Known execution notes

1. AZ-03 completed resource creation successfully but its final `az network vnet list` validation command failed because the installed Azure CLI required `--resource-group`. No network resources needed to be recreated.
2. The first AZ-04 attempt stopped at ACR creation because the installed CLI did not accept `--data-endpoint-enabled true` during `az acr create`.
3. AZ-04B continued safely, created the ACR and the remaining shared services, and completed successfully.
4. Do not rerun completed scripts unless the script is explicitly idempotent and the reason for rerunning is documented.

## Next action

Run AZ-05A to create geo-redundant document storage. After storage validation, proceed to AZ-05B for PostgreSQL Flexible Server, storage autogrow, primary-region HA, and the East US cross-region replica.
