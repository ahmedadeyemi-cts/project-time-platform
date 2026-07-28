# Azure Migration Security Controls

## Secret-handling rules

Never commit any of the following:

- Database passwords
- PostgreSQL connection strings containing credentials
- Microsoft Entra client secrets
- Brevo API keys
- Claude or other AI-provider API keys
- SMTP credentials
- TLS private keys
- Storage account keys or SAS tokens
- Azure service-principal secrets
- Cloudflare API tokens
- Local `.env` files
- Cloud Shell-generated files that contain secrets

Use Azure Key Vault and managed identity wherever possible. The regional application identities are the only identities that should receive runtime secret-read access.

## Repository-safe values

The following may be documented after review because they are identifiers rather than authentication secrets:

- Resource names
- Resource group names
- Subscription and tenant IDs
- Private address spaces
- Public ingress and egress IP addresses
- Azure regions
- DNS hostnames
- Managed identity names and resource IDs
- Deployment phase status

## Public access lifecycle

During migration, ACR, Key Vault, and Storage may retain temporary public management access. That access is transitional and must be removed after:

1. Container images are built and published.
2. Runtime secrets are loaded into both Key Vaults.
3. Private DNS resolution is validated from both Container Apps environments.
4. Managed-identity access is validated.
5. Migration data has been transferred.

The production target is private service access through VNets and private endpoints.

## Application ingress

Individual Container Apps replicas do not receive public IP addresses. Public access is provided through stable regional and global services:

- West US 3 Application Gateway WAF_v2
- East US Application Gateway WAF_v2
- Azure Front Door Premium with WAF

The Application Gateways will use the reserved static regional ingress IPs. Front Door will provide global health-based routing and the final public hostname.

## Cloudflare DNS process

Temporary regional records:

- `phd-west-test.onenecklab.com` -> West Application Gateway public IP
- `phd-east-test.onenecklab.com` -> East Application Gateway public IP

Controls:

1. Do not create a record before the Azure listener exists.
2. Start with Cloudflare proxy mode set to DNS only.
3. Validate Azure certificate binding, host-header routing, WAF behavior, backend health, API health, and redirects.
4. Enable Cloudflare proxying only if it is required and tested with Azure Front Door/Application Gateway behavior.
5. Use a CNAME to Azure Front Door for the final global hostname.

## Network controls

- PostgreSQL is private-only and uses delegated subnets.
- Key Vault, ACR, and Storage use private endpoints.
- Container Apps environments are internal.
- Application Gateway subnets are dedicated.
- NAT Gateways provide fixed regional outbound addresses.
- Route tables contain no forced-tunnel default route until a working Azure Firewall or other network virtual appliance exists.
- Reserved Azure Firewall and Gateway subnets must not host other resources.

## Database failover safety

Cross-region PostgreSQL replication is asynchronous. Do not allow both regional databases to accept writes simultaneously.

Initial regional database failover is a controlled operation:

1. Confirm primary-region unavailability.
2. Confirm the secondary replica's replication state and lag.
3. Quiesce or block writes to the old primary when possible.
4. Promote the secondary.
5. Redirect the virtual write endpoint or application connection target.
6. Validate application readiness before global traffic is redirected.
7. Record the promotion time, observed RPO, and recovery actions.

Automatic promotion must not be enabled until repeated failover drills demonstrate acceptable behavior.

## Logging and audit

Store deployment logs locally under `$HOME/project-health-dashboard-azure/logs`. Commit only sanitized summaries. Remove tokens, credentials, connection strings, and private certificate material before placing logs in GitHub.
