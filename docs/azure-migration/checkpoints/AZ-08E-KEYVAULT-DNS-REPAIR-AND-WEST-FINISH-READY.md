# AZ-08E — Key Vault DNS Repair and West Deployment Finish Ready

Date: 2026-07-12

## Confirmed state

- API and web images were built successfully in ACR.
- ACR private DNS was repaired.
- The API Container App image pull and managed-identity bootstrap succeeded.
- The API app received an internal FQDN.
- Key Vault references still failed during `az containerapp secret set`.
- The managed identity has `Key Vault Secrets User` at the West Key Vault scope.

## Root cause under repair

The West Key Vault private endpoint and private DNS zone are present, but the Container Apps environment still cannot retrieve the Key Vault secrets. AZ-08E repairs and validates:

- the private-endpoint DNS zone-group association
- the West VNet link to `privatelink.vaultcore.azure.net`
- the Key Vault A record matching the private-endpoint NIC address

## Continuation scope

AZ-08E does not rebuild images. It reuses the existing immutable API and web image digests, attaches the Key Vault references to the existing API app, creates the database-enabled revision, deploys the web app, and waits for both active revisions to become healthy.

No East PostgreSQL replica is created.
