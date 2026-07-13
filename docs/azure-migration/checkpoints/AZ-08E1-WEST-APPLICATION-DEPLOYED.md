# AZ-08E1 — West Application Deployed

Date: 2026-07-13

## Result

The Project Health Dashboard application was deployed successfully to the West US 3 Azure Container Apps environment.

### API

- Container App: `ca-phd-test-api-westus3`
- Ingress: internal
- FQDN: `ca-phd-test-api-westus3.internal.jollywave-6212cd8b.westus3.azurecontainerapps.io`
- Provisioning state: `Succeeded`
- Active revision health: `Healthy`
- Active revision running state: `Running`
- Database configuration revision: `Healthy`
- Key Vault references attached successfully

### Web

- Container App: `ca-phd-test-web-westus3`
- Ingress: external
- FQDN: `ca-phd-test-web-westus3.jollywave-6212cd8b.westus3.azurecontainerapps.io`
- Provisioning state: `Succeeded`
- Active revision health: `Healthy`
- Active revision running state: `Running`

### Images

- Source commit: `abf45bf824747767282f68fa5bd50909f9751eb0`
- API image digest: `sha256:e3a86765a4c228f1991c4dd828ab7f480dc4e35d3c3e0685c677c5fe29a72cb3`
- Web image digest: `sha256:dcc679be106c1e520070edfde835a663c6070244a25a957f02b0bdfe8166d745`

### Network repairs completed

- ACR private DNS records repaired for the global and regional data endpoints
- West Key Vault private DNS zone-group recreated
- West VNet Key Vault private-DNS link confirmed
- Key Vault A record confirmed at `10.30.5.7`

### Deferred item

The East US PostgreSQL replica remains deferred because the subscription is restricted from provisioning PostgreSQL in East US.
