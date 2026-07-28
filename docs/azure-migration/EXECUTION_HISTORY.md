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

The storage public network endpoint remains temporarily enabled for migration administration.

## 2026-07-12 — AZ-05B PostgreSQL primary

After correcting Azure CLI and SKU-discovery differences, the West US 3 PostgreSQL Flexible Server primary was created and validated:

- Server `pg-phd-test-w3-7825cc`
- PostgreSQL 16
- General Purpose `Standard_D2ds_v4`
- 128 GiB Premium LRS with autogrow
- 35-day backup retention and geo-redundant backup
- Private delegated subnet and private DNS
- Same-zone high availability because West US 3 could not place the standby in a separate zone
- Database `project_health_dashboard`
- Public network access disabled

The administrator secret remains in the regional Key Vaults and was not committed.

## 2026-07-12 — AZ-05C1 source database export

A PostgreSQL 13 custom-format export of `ProjectPulse` was generated on the Oracle Linux source host and uploaded to the private `database-exports` container using a short-lived user-delegation SAS. Fifteen export artifacts were verified. The source system remained active, so this is an initial seed export rather than the final write-freeze export.

## 2026-07-12 — AZ-05C2 restore and validation

A temporary private Rocky Linux 10.2 migration VM was created in East US. The first restore attempt stopped before contacting PostgreSQL because of private DNS defects for Blob Storage and Key Vault. The evidence was preserved, and the DNS zone groups and VNet links were repaired.

The clean retry completed successfully:

- Restore command exit code: 0
- Schemas: 1
- Tables: 170
- Extensions: 2
- Errors: 0
- Warnings: 0
- Required evidence files: 12 of 12
- Validation comparison: passed

Temporary upload permissions were removed, and the migration VM was deallocated.

## 2026-07-12 — AZ-05C3 East US PostgreSQL replica

The corrected preflight confirmed that East US advertises the planned PostgreSQL 16 `Standard_D2ds_v4` configuration. The actual replica create request was rejected with `The location is restricted from performing this operation.`

Diagnostics confirmed a subscription-level regional provisioning restriction. The subscription was upgraded from Free Trial to Pay-As-You-Go, but portal and CLI support-ticket attempts did not produce a ticket. The East US replica was therefore deferred. No replica resource exists and no replica billing started.

## 2026-07-12 — AZ-06A/B West Container Apps environment

Created and validated the internal West US 3 Container Apps managed environment:

- Environment `cae-phd-test-westus3`
- Provisioning state `Succeeded`
- Internal mode enabled
- Infrastructure subnet `vnet-phd-test-westus3/snet-aca-infrastructure`
- Generated domain `jollywave-6212cd8b.westus3.azurecontainerapps.io`
- Static internal IP `10.30.0.167`

The first status check compared `West US 3` to `westus3` literally and reported a false pending result. The validation script was corrected to normalize Azure location names. No environment repair was required.

## 2026-07-12 — AZ-06C West Container Apps private DNS

Created and validated:

- Private DNS zone `jollywave-6212cd8b.westus3.azurecontainerapps.io`
- West VNet link state `Completed`
- East VNet link state `Completed`
- Wildcard A record `*` pointing to `10.30.0.167`

No application container, public DNS record, Cloudflare record, or East PostgreSQL replica was created.

## Current checkpoint

Infrastructure is complete through AZ-06C for the West internal application platform. The next action is the read-only AZ-07A source-code checkpoint on the Oracle Linux source host. Application images must not be built until the existing uncommitted source changes are reviewed, sanitized, committed, and pushed.