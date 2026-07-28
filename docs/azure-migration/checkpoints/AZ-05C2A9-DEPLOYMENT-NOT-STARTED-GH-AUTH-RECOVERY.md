# AZ-05C2A9 — Deployment Not Started; GitHub Authentication Recovery Required

**Date:** 2026-07-12

## Current state

- `Microsoft.Compute`: Registered
- `Microsoft.Quota`: Registered
- East US Daldsv7 quota: limit 4, usage 0, applicable
- Migration resource group: not created
- East US migration NIC: not created
- East US migration VM: not created
- Billable migration compute: not started

## Immediate next action

Reauthenticate GitHub CLI in Azure Cloud Shell, verify access to the private repository, then download and run:

`deployment/azure/scripts/az05c2a9-submit-eastus-rocky10-restore-runner.sh`

The interactive wrapper must not use top-level `exit` commands.
