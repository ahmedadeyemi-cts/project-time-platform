# Project Health Dashboard Azure Migration

This directory is the authoritative runbook for migrating the Project Health Dashboard from its current Oracle Linux host to a redundant, autoscaling Azure architecture.

## Naming

The product name is **Project Health Dashboard**. New Azure resources use the short prefix `phd`. The retired ProjectPulse name must not be used for new Azure resources, scripts, documentation, DNS names, or deployment outputs.

## Target architecture

The target is a two-region Azure deployment:

- Primary region: `westus3`
- Secondary region: `eastus`
- Global ingress: Azure Front Door Premium with WAF
- Regional ingress: one Application Gateway WAF_v2 per region
- Application runtime: internal Azure Container Apps environments in both regions
- Application scaling: horizontal replica autoscaling
- Database: Azure Database for PostgreSQL Flexible Server
- Database resilience: zone-redundant HA in the primary region plus an East US cross-region replica
- Object storage: RA-GZRS StorageV2 with versioning, soft delete, lifecycle management, and regional private endpoints
- Container images: Azure Container Registry Premium with West US 3 and East US replicas
- Secrets: regional Azure Key Vaults accessed with managed identities
- Monitoring: regional Log Analytics and Application Insights
- Regional egress: one NAT Gateway and one static egress IP per region

## Availability model

Application replicas can scale horizontally and are disposable. Public IP addresses are attached to stable regional ingress services, not to individual Container Apps replicas.

The architecture uses four regional static IP addresses:

- West ingress IP for the West Application Gateway
- East ingress IP for the East Application Gateway
- West egress IP for the West Container Apps subnet through NAT Gateway
- East egress IP for the East Container Apps subnet through NAT Gateway

The regional Cloudflare records remain DNS-only until HTTPS, WAF, health probes, host headers, and backend routing have been validated. The final global hostname will use a CNAME to Azure Front Door.

## Migration principles

1. Keep the current production host available until Azure validation and cutover are complete.
2. Pause feature development except for migration-enabling changes.
3. Never commit passwords, client secrets, API keys, private certificates, connection strings, `.env` files, or Cloud Shell generated configuration files.
4. Use managed identities and Key Vault references in Azure.
5. Build the Azure environment through version-controlled scripts and, where practical, convert stable scripts into Bicep modules.
6. Run each phase independently and validate it before proceeding.
7. Do not create Cloudflare DNS records until a corresponding Azure listener is healthy.
8. Do not automate PostgreSQL regional promotion until failover drills establish an acceptable RPO and RTO.

## Runbook phases

| Phase | Purpose | Status |
|---|---|---|
| AZ-01 | Source-server discovery | Completed |
| AZ-02A | Azure subscription and region discovery | Completed |
| AZ-03 | Two-region network foundation | Completed; validation-command-only issue documented |
| AZ-03B | Regional ingress and egress IP foundation | Completed |
| AZ-04 | Monitoring and identities | Completed |
| AZ-04B | ACR, Key Vault, RBAC, and private endpoints | Completed |
| AZ-05A | Geo-redundant document storage | Next |
| AZ-05B | PostgreSQL primary, HA, autogrow, and cross-region replica | Planned |
| AZ-06 | Container image build and registry publication | Planned |
| AZ-07 | Two internal Container Apps environments | Planned |
| AZ-08 | Regional Application Gateways and temporary DNS | Planned |
| AZ-09 | Database and document migration rehearsal | Planned |
| AZ-10 | Azure Front Door Premium and WAF | Planned |
| AZ-11 | Final sync and DNS cutover | Planned |
| AZ-12 | DR drill, observation, and source retirement | Planned |

## Repository layout

- `docs/azure-migration/STATUS.md`: actual resources, IPs, decisions, and phase results
- `docs/azure-migration/SECURITY.md`: secret-handling, network exposure, and DNS controls
- `deployment/azure/README.md`: operator instructions
- `deployment/azure/scripts/`: rerunnable Azure and source-discovery scripts

## Generated local files

The scripts write logs and non-secret configuration files under:

```text
$HOME/project-health-dashboard-azure/
```

That directory is operational state and must not be committed. Values from it may be copied into sanitized documentation only after review.
