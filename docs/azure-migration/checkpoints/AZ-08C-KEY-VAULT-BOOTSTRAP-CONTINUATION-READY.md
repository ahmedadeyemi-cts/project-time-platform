# AZ-08C — Key Vault Bootstrap Continuation Ready

Date: 2026-07-12

## Completed before failure

- API image built and pushed to ACR
- web image built and pushed to ACR
- image digests resolved
- West managed identity retained AcrPull and Key Vault Secrets User
- PostgreSQL connection-string secret created in the West Key Vault

## Failure

The first API Container App creation failed while resolving the two Key Vault secret references through the user-assigned managed identity. No healthy API revision or web Container App was created.

## Continuation

`deployment/azure/scripts/az08c-continue-west-deployment-keyvault-bootstrap.sh` reuses the existing immutable images and performs a two-stage API deployment:

1. remove the failed API shell when present
2. create the API app with the user-assigned identity and no Key Vault references
3. wait for identity binding propagation
4. attach the Key Vault references
5. add the database secret references in a new revision
6. wait for a healthy API revision
7. deploy and validate the West web app

The continuation does not rebuild images, create an East PostgreSQL replica, expose secret values, or modify the source repository.
