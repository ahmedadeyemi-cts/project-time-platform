# Azure Migration Execution History

This log records the migration actions completed through Azure Cloud Shell and the source host. It intentionally excludes passwords, API keys, connection strings, certificate private keys, and other secret values.

## 2026-07-11 — AZ-01 source discovery

Source host discovery confirmed:

- Oracle Linux Server 9.7
- 2 vCPUs
- Approximately 945 MiB RAM
- PostgreSQL 13.23
- Approximately 31 MB database
- API health HTTP 200
- Public frontend HTTP 200
- Local document/upload paths and systemd timers require migration
- Repository HEAD `5a221da29cdfc1134e5d603175b311ff97658b67`
- Uncommitted application changes existed and remain subject to a separate source checkpoint

No source files, database rows, services, DNS records, or Azure resources were changed.

## 2026-07-11 — AZ-02A Azure discovery

Confirmed:

- Subscription `Azure subscription 1`
- West US 3 is available
- No existing Azure resources or resource groups
- Required providers were initially unregistered

No resources were created.

## 2026-07-11 — AZ-03 network foundation

Created:

- Eight Project Health Dashboard resource groups
- West US 3 VNet `10.30.0.0/16`
- East US VNet `10.40.0.0/16`
- Container Apps, PostgreSQL, private endpoint, management, Azure Firewall-reserved, and Gateway-reserved subnets
- Regional NSGs
- Regional route tables with no custom routes
- Bidirectional global VNet peering
- Shared private DNS zones linked to both VNets

Execution note: resource creation succeeded. The final validation command `az network vnet list` failed because the installed CLI required `--resource-group`. The canonical repository script uses regional `az network vnet show` validation instead.

## 2026-07-11 — AZ-03B regional IP and NAT foundation

Created:

- West Application Gateway subnet `10.30.8.0/24`
- East Application Gateway subnet `10.40.8.0/24`
- West regional ingress IP `20.118.180.129`
- East regional ingress IP `52.186.166.195`
- West regional egress IP `20.106.109.188`
- East regional egress IP `20.88.160.79`
- West NAT Gateway `nat-phd-test-aca-westus3`
- East NAT Gateway `nat-phd-test-aca-eastus`

Both NAT Gateways were attached to the regional Container Apps infrastructure subnets. No Application Gateway, Container Apps environment, or DNS record was created.

## 2026-07-11 — AZ-04 first attempt

Created successfully before the stop:

- West and East Log Analytics workspaces
- West and East Application Insights resources
- West and East application managed identities

The script then stopped because the installed Azure CLI rejected `--data-endpoint-enabled true` on `az acr create`.

No ACR, Key Vault, or private endpoint was created during this attempt.

## 2026-07-11 — AZ-04B shared-services continuation

Created and validated:

- Premium ACR `acrphdtest7825cc`
- Zone-redundant ACR primary in West US 3
- Zone-redundant ACR geo-replica in East US
- West Key Vault `kv-phd-t-w3-7825cc`
- East Key Vault `kv-phd-t-eus-7825cc`
- `AcrPull` role assignments for both application identities
- Regional `Key Vault Secrets User` role assignments
- Administrator `Key Vault Administrator` role assignments
- Regional ACR private endpoints
- Regional Key Vault private endpoints
- Private DNS zone groups

ACR and Key Vault public network access remain temporarily enabled for migration preparation. The canonical AZ-04 script in this repository incorporates the CLI compatibility correction.

## 2026-07-11 — AZ-05A storage foundation

Created and validated:

- RA-GZRS StorageV2 account `stphdtest7825cc`
- Primary region West US 3
- Secondary region East US
- Blob public access disabled
- Shared-key authorization disabled
- Minimum TLS 1.2
- Blob versioning enabled
- Blob soft delete: 30 days
- Container soft delete: 30 days
- Change feed enabled: 90 days
- Last-access tracking enabled
- Project-document lifecycle tiering to Cool after 90 days
- Migration/backup lifecycle tiering to Cool after 30 days
- Regional Blob private endpoints
- Regional managed-identity Blob Data Contributor access

Private containers:

- `project-documents`
- `work-register-intake`
- `migration-staging`
- `database-exports`
- `application-backups`

The storage public network endpoint remains temporarily enabled for migration administration. No database, Container Apps environment, Application Gateway, or Cloudflare DNS record was created.

## Current checkpoint

Infrastructure completed through AZ-05A. The next infrastructure phase is AZ-05B PostgreSQL Flexible Server. Before building an application image, the source server's uncommitted application changes must be committed and pushed through a separate source checkpoint.
