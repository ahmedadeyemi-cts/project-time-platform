# AZ-05C2A9 — GitHub CLI Authentication Lost and Interactive Exit Closed Cloud Shell

**Date:** 2026-07-12

## Summary

The East US Rocky Linux 10 restore-runner deployment did not start. The Azure Cloud Shell session no longer had GitHub CLI authentication, so the private repository script download failed with:

`To get started with GitHub CLI, please run: gh auth login`

The interactive wrapper also contained `exit 1`. Because the wrapper was pasted directly into the interactive Cloud Shell, that exit terminated the user session when the download failed.

## Impact

- No migration resource group was created.
- No NIC was created.
- No VM was created.
- No billable migration compute started.
- East US quota remains ready: Standard Daldsv7 Family vCPUs limit 4, usage 0.

## Corrective Action

1. Reauthenticate GitHub CLI in Cloud Shell.
2. Verify `gh auth status`.
3. Download the canonical deployment script from the private repository.
4. Use an interactive wrapper with no top-level `exit` statements.
5. Execute the downloaded file as a child Bash process so any script exit cannot terminate the parent Cloud Shell.

## Canonical deployment script

`deployment/azure/scripts/az05c2a9-submit-eastus-rocky10-restore-runner.sh`

## Safety rule

Do not paste `exit` into the interactive Cloud Shell control wrapper. Reserve `exit` for standalone child scripts invoked with `bash script.sh`.
