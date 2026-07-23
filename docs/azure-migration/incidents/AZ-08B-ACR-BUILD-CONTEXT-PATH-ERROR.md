# AZ-08B — ACR Build Context Path Error

Date: 2026-07-12

## Result

The first AZ-08B execution stopped before any ACR build submission because Azure CLI attempted to resolve `deployment/containers/api/Dockerfile` relative to the Cloud Shell working directory instead of the cloned source context.

Observed error:

`ERROR: Unable to find 'deployment/containers/api/Dockerfile'.`

## Impact

- no API image was built
- no web image was built
- no Container App was created
- no Key Vault connection-string secret was created
- no Azure application revision was started

## Correction

Added `deployment/azure/scripts/az08b1-build-and-deploy-west-application-context-fix.sh`.

The wrapper downloads the canonical AZ-08B script, changes into the cloned source directory before both `az acr build` commands, changes the build context arguments to `.`, validates the corrected script syntax, and then executes the same guarded West application deployment.
