# AZ-08D — ACR Private DNS Repair Ready

Date: 2026-07-12

## Current state

The API and web images were successfully built and pushed to `acrphdtest7825cc`.

The API Container App provisioning attempt failed before a healthy revision was created because the West Container Apps environment could not resolve `acrphdtest7825cc.azurecr.io`. The platform returned a DNS `no such host` error while resolving the digest-pinned API image.

## Diagnosis

The West VNet uses the `privatelink.azurecr.io` private DNS zone, but the required ACR private endpoint records are missing or incorrect. Azure Container Registry private endpoint clients require DNS records for the global registry endpoint and regional data endpoints.

## Repair action

`deployment/azure/scripts/az08d-repair-acr-private-dns-and-continue-west-deployment.sh` will:

1. inspect the West ACR private endpoint NIC
2. derive the global and regional ACR endpoint names and private IP addresses
3. confirm the private DNS zone and West VNet link
4. repair only the A records associated with `acrphdtest7825cc`
5. validate every repaired record
6. wait for DNS propagation and resolver cache expiration
7. invoke AZ-08C to continue the deployment using the existing API and web images

The script does not rebuild images and does not create the East PostgreSQL replica.
